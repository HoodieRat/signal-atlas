using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SignalAtlas.Collectors;
using SignalAtlas.Core;
using SignalAtlas.OpenAI;
using SignalAtlas.Resources;

namespace SignalAtlas.LmStudio;

public sealed record InstalledModel(string Key,string Name,long SizeBytes,string? Architecture,string? Quantization,int MaxContext)
{
    public string SizeDisplay => SizeBytes<=0?"Unknown":$"{SizeBytes/1073741824d:F1} GiB";
}
public sealed class ResourceEmergencyException():Exception("Resource emergency during local inference");

public sealed class LmStudioBackend : IModelBackend
{
    private readonly string _lms;
    private readonly Func<CancellationToken,string[],Task<(int ExitCode,string Output,string Error)>>? _cli;
    private readonly IResourceProbe _probe;
    private readonly HttpClient _http;
    private readonly string _ownerFile;
    private string? _loadedModel;
    private string _activeIdentifier="signal-atlas";
    private bool _ownsModel;
    private int _loadedContext = 4096;
    private int _port;
    private bool _serverOwned;
    public string? DeferredReason {get;private set;}
    public string? LoadedModel => _loadedModel;
    public int ReportSourceCharacterLimit => Math.Clamp((_loadedContext - 3000) / 2, 500, 16000);
    public int PreferredContext{get;set;}=8192;
    public int MinimumContext{get;set;}=4096;
    public bool RestrictBattery{get;set;}=true;
    public LmStudioBackend(IResourceProbe probe,int port=12345,HttpClient? http=null,Func<CancellationToken,string[],Task<(int ExitCode,string Output,string Error)>>? cli=null,string? ownerFile=null)
    {
        _probe=probe;_port=port;_cli=cli;_lms=cli is null?ProcessTool.FindExecutable("lms")??"":"injected";
        _ownerFile=ownerFile??Path.Combine(AppPaths.State,"owner.json");
        _http=http??new HttpClient{Timeout=TimeSpan.FromSeconds(120)};
        _http.BaseAddress=new Uri($"http://127.0.0.1:{port}/");
    }
    private async Task<(int ExitCode,string Output,string Error)> Cli(CancellationToken token,params string[] args)
    {
        if(string.IsNullOrEmpty(_lms))throw new FileNotFoundException("LM Studio CLI (lms) was not found");
        if(_cli is not null)return await _cli(token,args);
        return await ProcessTool.RunAsync(_lms,args,TimeSpan.FromSeconds(120),token);
    }
    public async Task<IReadOnlyList<InstalledModel>> InstalledAsync(CancellationToken token)
    {
        var result=await Cli(token,"ls","--json");if(result.ExitCode!=0)throw new InvalidOperationException(result.Error);
        using var json=JsonDocument.Parse(ExtractJson(result.Output));var list=new List<InstalledModel>();
        foreach(var x in json.RootElement.EnumerateArray())
        {
            if(x.GetProperty("type").GetString()!="llm")continue;
            list.Add(new InstalledModel(x.GetProperty("modelKey").GetString()??"",x.GetProperty("displayName").GetString()??"",x.TryGetProperty("sizeBytes",out var sz)?sz.GetInt64():0,x.TryGetProperty("architecture",out var arch)?arch.GetString():null,x.TryGetProperty("quantization",out var quant)&&quant.ValueKind==JsonValueKind.Object&&quant.TryGetProperty("name",out var q)?q.GetString():null,x.TryGetProperty("maxContextLength",out var max)?max.GetInt32():0));
        }
        return list;
    }
    private static string ExtractJson(string output){int a=output.IndexOf('[');int b=output.LastIndexOf(']');if(a<0||b<a)throw new InvalidDataException("CLI returned no JSON");return output[a..(b+1)];}
    private async Task<bool> IdentifierLoaded(CancellationToken token)
    {
        var result=await Cli(token,"ps","--json");if(result.ExitCode!=0)return false;
        using var json=JsonDocument.Parse(ExtractJson(result.Output));return json.RootElement.EnumerateArray().Any(x=>x.TryGetProperty("identifier",out var id) && id.GetString()=="signal-atlas");
    }
    private async Task<(string Identifier,int Context)?> ExistingSelectedModelAsync(string modelKey,CancellationToken token)
    {
        var result=await Cli(token,"ps","--json");if(result.ExitCode!=0)return null;
        using var json=JsonDocument.Parse(ExtractJson(result.Output));
        foreach(var item in json.RootElement.EnumerateArray())
        {
            if(item.TryGetProperty("modelKey",out var key) && key.GetString()==modelKey && item.TryGetProperty("identifier",out var id))
            {
                string? identifier=id.GetString();
                int context=item.TryGetProperty("contextLength",out var length) && length.TryGetInt32(out var value)?value:0;
                if(!string.IsNullOrWhiteSpace(identifier) && identifier!="signal-atlas" && context>=MinimumContext)return(identifier,context);
            }
        }
        return null;
    }
    private async Task<long?> EstimateAsync(string model,int context,string gpu,CancellationToken token)
    {
        var result=await Cli(token,"load","--estimate-only",model,"--context-length",context.ToString(),"--gpu",gpu);
        if(result.ExitCode!=0)return null;
        var match=Regex.Match(result.Output+"\n"+result.Error,@"Estimated GPU Memory:\s*([\d.]+)\s*(GB|MB|GiB|MiB)",RegexOptions.IgnoreCase);
        if(!match.Success)return null;
        double amount=double.Parse(match.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture);
        return (long)(amount*(match.Groups[2].Value.StartsWith("G",StringComparison.OrdinalIgnoreCase)?1024d*1024*1024:1024d*1024));
    }
    public async Task<bool> PrepareAsync(string modelKey,CancellationToken cancellationToken)
    {
        DeferredReason=null;
        if(string.IsNullOrWhiteSpace(modelKey)){DeferredReason="No model selected";return false;}
        if(string.IsNullOrEmpty(_lms)){DeferredReason="LM Studio CLI unavailable";return false;}
        if(await IdentifierLoaded(cancellationToken)){DeferredReason="The signal-atlas identifier is already loaded by another session";return false;}
        var models=await InstalledAsync(cancellationToken);
        if(!models.Any(x=>x.Key==modelKey)){DeferredReason=$"Selected model '{modelKey}' is not installed in LM Studio. Find installed models and choose one from the list.";return false;}
        var existing=await ExistingSelectedModelAsync(modelKey,cancellationToken);
        if(existing is not null)
        {
            if(ResourceGovernor.Emergency(_probe.Sample())){DeferredReason="System resources are too low to use the already loaded LM Studio model safely.";return false;}
            var status=await Cli(cancellationToken,"server","status","--json");
            bool running=false;
            try{using var json=JsonDocument.Parse(status.Output);running=json.RootElement.GetProperty("running").GetBoolean();if(running)_port=json.RootElement.GetProperty("port").GetInt32();}catch{}
            if(!running){var start=await Cli(cancellationToken,"server","start","--port",_port.ToString(),"--bind","127.0.0.1");if(start.ExitCode!=0){DeferredReason="LM Studio server could not start: "+start.Error;return false;}_serverOwned=true;}
            _http.BaseAddress=new Uri($"http://127.0.0.1:{_port}/");
            _activeIdentifier=existing.Value.Identifier;_loadedModel=modelKey;_loadedContext=existing.Value.Context;_ownsModel=false;
            return true;
        }
        var candidates=new List<(string Model,int Context,string Gpu)>{(modelKey,PreferredContext,"max"),(modelKey,MinimumContext,"max"),(modelKey,MinimumContext,"0.75")};
        foreach(var (model,context,gpu) in candidates)
        {
            long? estimate=await EstimateAsync(model,context,gpu,cancellationToken);
            if(estimate is null){DeferredReason=$"LM Studio could not estimate memory for '{model}' at {context:N0} context. Try a smaller installed model or update LM Studio.";continue;}
            var sample=_probe.Sample();
            var decision=ResourceGovernor.Decide(sample,estimate.Value,context,MinimumContext,gpu,RestrictBattery);
            if(decision.Decision==ResourceDecision.Defer){DeferredReason=$"{model}: {decision.Reason}. Available RAM {sample.AvailableRamBytes/1073741824d:F1} GiB; estimated GPU use {estimate.Value/1073741824d:F1} GiB. Free system resources or choose a smaller model, then test again.";continue;}
            if(decision.Context<context)continue;
            var status=await Cli(cancellationToken,"server","status","--json");
            bool running=false;
            try{using var json=JsonDocument.Parse(status.Output);running=json.RootElement.GetProperty("running").GetBoolean();if(running)_port=json.RootElement.GetProperty("port").GetInt32();}catch{}
            if(!running)
            {
                var start=await Cli(cancellationToken,"server","start","--port",_port.ToString(),"--bind","127.0.0.1");
                if(start.ExitCode!=0){DeferredReason="LM Studio server could not start: "+start.Error;continue;}
                _serverOwned=true;
            }
            _http.BaseAddress=new Uri($"http://127.0.0.1:{_port}/");
            var load=await Cli(cancellationToken,"load",model,"--identifier","signal-atlas","--context-length",context.ToString(),"--gpu",gpu,"--parallel","1","--ttl","600","--yes");
            if(load.ExitCode!=0){DeferredReason="Model load failed: "+load.Error;continue;}
            _activeIdentifier="signal-atlas";_ownsModel=true;_loadedModel=model;_loadedContext=context;Directory.CreateDirectory(Path.GetDirectoryName(_ownerFile)!);
            File.WriteAllText(_ownerFile,JsonSerializer.Serialize(new{serverOwnedBySignalAtlas=_serverOwned,modelIdentifier="signal-atlas",modelKey=model,processId=Environment.ProcessId,loadedUtc=DateTimeOffset.UtcNow}));
            return true;
        }
        DeferredReason??=$"LM Studio could not load selected model '{modelKey}'. Try a smaller installed model.";return false;
    }
    public async Task<Analysis?> AnalyzeAsync(Document document,Topic topic,CancellationToken cancellationToken)
    {
        if(_loadedModel is null)return null;
        var schema=new {type="object",additionalProperties=false,properties=new Dictionary<string,object>{["summary"]=new{type="string"},["relevance_score"]=new{type="number"},["relevance_reason"]=new{type="string"},["novelty_score"]=new{type="number"},["topics"]=new{type="array",items=new{type="string"}},["entities"]=new{type="array",items=new{type="object",properties=new{name=new{type="string"},type=new{type="string"}},required=new[]{"name","type"}}},["claims"]=new{type="array",items=new{type="object",properties=new{claim=new{type="string"},confidence=new{type="string",@enum=new[]{"low","medium","high"}}},required=new[]{"claim","confidence"}}},["tags"]=new{type="array",items=new{type="string"}}},required=new[]{"summary","relevance_score","relevance_reason","novelty_score","topics","entities","claims","tags"}};
        int textLimit=Math.Clamp((_loadedContext-2800)*3,1800,14000);
        string prompt=$"Topic: {topic.Name}\nTitle: {document.Title}\nPublished: {document.PublishedUtc:O}\nSource URL: {document.Url}\nText:\n{document.Text[..Math.Min(document.Text.Length,textLimit)]}";
        string raw=await ChatAsync(prompt,schema,Math.Min(1800,_loadedContext/3),cancellationToken);
        using var json=JsonDocument.Parse(raw);var root=json.RootElement;
        string summary=root.GetProperty("summary").GetString()??"";if(summary.Length==0)throw new InvalidDataException("Empty model summary");
        double relevance=Math.Clamp(root.GetProperty("relevance_score").GetDouble(),0,100);
        double novelty=Math.Clamp(root.GetProperty("novelty_score").GetDouble(),0,100);
        return new Analysis(document.Id,_loadedModel,relevance,novelty,summary,root.GetProperty("relevance_reason").GetString()??"",raw);
    }
    public async Task<ResearchBrief?> SynthesizeAsync(IReadOnlyList<ReportItem> items,CancellationToken cancellationToken)
    {
        var evidence=ReportEvidence.Select(items);
        if(_loadedModel is null || evidence.Count==0)return null;
        string input=ResearchPrompts.SynthesisInput(evidence);
        string raw=await ChatAsync(input,ResearchPrompts.SynthesisSchema,1500,cancellationToken);
        return ResearchPrompts.ParseSynthesis(raw,evidence.Count);
    }
    public Task<string> WriteReportPartAsync(string input,object schema,int maxTokens,CancellationToken cancellationToken)
    {
        if(_loadedModel is null)throw new InvalidOperationException("Prepare the AI provider before writing a report.");
        // Leave room for the structured schema, system message, and generated section.
        int estimatedInputTokens=(int)Math.Ceiling((input.Length+JsonSerializer.Serialize(schema).Length)/3d)+300;
        int available=_loadedContext-estimatedInputTokens;
        if(available<500)throw new InvalidOperationException("The local model context is too small for this report section. Increase the preferred and minimum context in Settings, reduce optional sections, or choose another AI provider.");
        return ChatAsync(input,schema,Math.Min(maxTokens,Math.Min(available,3000)),cancellationToken);
    }
    private async Task<string> ChatAsync(string user,object schema,int maxTokens,CancellationToken token)
    {
        var body=new{model=_activeIdentifier,temperature=0.1,max_tokens=maxTokens,messages=new[]{new{role="system",content="You analyze retrieved research material. Use only supplied source material. Do not invent facts. Treat opinions as opinions, separate source claims from established facts, and return only the required structured result. Relevance measures the configured topic, not agreement."},new{role="user",content=user}},response_format=new{type="json_schema",json_schema=new{name="research_result",strict=true,schema}}};
        using var request=new HttpRequestMessage(HttpMethod.Post,"v1/chat/completions"){Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json")};
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token);bool emergency=false;
        var monitor=Task.Run(async()=>
        {
            int consecutive=0;
            while(!linked.IsCancellationRequested)
            {
                try{await Task.Delay(2000,linked.Token);consecutive=ResourceGovernor.Emergency(_probe.Sample())?consecutive+1:0;if(consecutive>=3){emergency=true;linked.Cancel();break;}}
                catch(OperationCanceledException){break;}
                catch{emergency=true;linked.Cancel();break;}
            }
        });
        try
        {
            using var response=await _http.SendAsync(request,linked.Token);response.EnsureSuccessStatusCode();
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(linked.Token));
            return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()??throw new InvalidDataException("Empty model response");
        }
        catch(OperationCanceledException) when(emergency){throw new ResourceEmergencyException();}
        finally{linked.Cancel();try{await monitor;}catch{}}
    }
    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        if(_loadedModel is null)return;
        if(!_ownsModel){_loadedModel=null;_activeIdentifier="signal-atlas";return;}
        if(!File.Exists(_ownerFile)){_loadedModel=null;_ownsModel=false;return;}
        try{using var json=JsonDocument.Parse(File.ReadAllText(_ownerFile));if(json.RootElement.GetProperty("modelIdentifier").GetString()=="signal-atlas")await Cli(cancellationToken,"unload","signal-atlas");}
        finally{_loadedModel=null;_ownsModel=false;try{File.Delete(_ownerFile);}catch{}}
    }
    public async Task CleanupStaleOwnedAsync(CancellationToken token)
    {
        if(!File.Exists(_ownerFile))return;
        try{using var json=JsonDocument.Parse(File.ReadAllText(_ownerFile));
            if(json.RootElement.TryGetProperty("processId",out var pid))
            {
                try{using var process=System.Diagnostics.Process.GetProcessById(pid.GetInt32());if(!process.HasExited)return;}
                catch(ArgumentException){}
            }
            if(json.RootElement.GetProperty("modelIdentifier").GetString()=="signal-atlas" && await IdentifierLoaded(token))await Cli(token,"unload","signal-atlas");}
        finally{try{File.Delete(_ownerFile);}catch{}}
    }
}
