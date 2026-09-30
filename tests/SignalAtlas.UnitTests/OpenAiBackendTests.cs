using System.Net;
using System.Text;
using System.Text.Json;
using SignalAtlas.Core;
using SignalAtlas.OpenAI;

namespace SignalAtlas.UnitTests;

public sealed class OpenAiBackendTests
{
    private static readonly Topic Topic = new(1,"animation","",50,168,30,true,[]);
    private static readonly Document Document = new(2,1,"https://example.org/article","Example article",
        "An example about animation research.","hash",null,DateTimeOffset.UtcNow,true,1,1);
    private const string AnalysisJson = "{\"summary\":\"A source-based summary.\",\"relevance_score\":82,\"relevance_reason\":\"Matches animation\",\"novelty_score\":64}";

    [Fact] public void SynthesisRequiresValidSourceCitations()
    {
        const string valid="{\"executive_summary\":\"A theme.\",\"findings\":[{\"heading\":\"Finding\",\"analysis\":\"A sourced claim.\",\"source_ids\":[1]}],\"implications\":\"Consider a test.\",\"limitations\":\"One source.\"}";
        Assert.Single(ResearchPrompts.ParseSynthesis(valid,1).Findings);
        Assert.Throws<InvalidDataException>(()=>ResearchPrompts.ParseSynthesis(valid.Replace("[1]","[2]"),1));
    }

    [Fact] public async Task ApiBackendSendsStructuredRequestWithoutSavingKey()
    {
        var handler=new RecordingHandler();using var http=new HttpClient(handler);
        var backend=new OpenAiApiBackend(http,()=>"test-only-key");
        Assert.True(await backend.PrepareAsync("gpt-6-luna",CancellationToken.None));
        var result=await backend.AnalyzeAsync(Document,Topic,CancellationToken.None);
        Assert.Equal("A source-based summary.",result?.Summary);
        Assert.Equal("openai-api:gpt-6-luna",result?.ModelKey);
        Assert.Equal("https://api.openai.com/v1/responses",handler.Uri);
        Assert.Equal("Bearer test-only-key",handler.Authorization);
        Assert.Contains("\"store\":false",handler.Body);
        Assert.Contains("\"json_schema\"",handler.Body);
        Assert.DoesNotContain("test-only-key",handler.Body);
        await backend.CleanupAsync(CancellationToken.None);
    }

    [Fact] public async Task ApiBackendRequiresRuntimeKey()
    {
        var backend=new OpenAiApiBackend(keyProvider:()=>null);
        Assert.False(await backend.PrepareAsync("gpt-6-luna",CancellationToken.None));
        Assert.Contains("OPENAI_API_KEY",backend.DeferredReason);
    }

    [Fact] public async Task ApiSynthesisUsesSourceMetadataAndReturnsCitedFindings()
    {
        var handler=new RecordingHandler();using var http=new HttpClient(handler);
        var backend=new OpenAiApiBackend(http,()=>"test-only-key");
        Assert.True(await backend.PrepareAsync("gpt-6-luna",CancellationToken.None));
        var analysis=new Analysis(Document.Id,"test",82,64,"A source-based summary.","Matches animation","{}");
        var source=new Source(1,"Example source","rss",null,"automated_direct",true,null);
        var brief=await backend.SynthesizeAsync([new ReportItem(Document,Topic,source,analysis)],CancellationToken.None);
        Assert.Equal("A theme.",brief?.ExecutiveSummary);
        Assert.Equal([1],brief?.Findings[0].SourceIds);
        Assert.Contains("Example article",handler.Body);
        Assert.Contains("https://example.org/article",handler.Body);
    }

    [Fact] public async Task CodexBackendUsesChatGptLoginAndEphemeralReadOnlyRun()
    {
        string[]? execArgs=null;string? prompt=null;
        Task<(int ExitCode,string Output)> Run(string[] args,string? input,CancellationToken _)
        {
            if(args.SequenceEqual(["login","status"]))return Task.FromResult((0,"Logged in using ChatGPT"));
            execArgs=args;prompt=input;return Task.FromResult((0,AnalysisJson));
        }
        var backend=new CodexCliBackend(Run);
        Assert.True(await backend.PrepareAsync("codex-chatgpt",CancellationToken.None));
        var result=await backend.AnalyzeAsync(Document,Topic,CancellationToken.None);
        Assert.Equal("codex-chatgpt",result?.ModelKey);
        Assert.Contains("--ephemeral",execArgs!);
        Assert.Contains("read-only",execArgs!);
        Assert.Contains("--ignore-user-config",execArgs!);
        Assert.Contains("Do not use tools",prompt);
        Assert.Contains("Example article",prompt);
    }

    [Fact] public async Task CodexBackendRejectsApiKeyLoginForChatGptMode()
    {
        var backend=new CodexCliBackend((_,_,_)=>Task.FromResult((0,"Logged in using an API key")));
        Assert.False(await backend.PrepareAsync("codex-chatgpt",CancellationToken.None));
        Assert.Contains("ChatGPT",backend.DeferredReason);
    }

    private sealed class RecordingHandler:HttpMessageHandler
    {
        public string? Uri,Authorization,Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Uri=request.RequestUri?.ToString();Authorization=request.Headers.Authorization?.ToString();Body=await request.Content!.ReadAsStringAsync(token);
            string synthesis="{\"executive_summary\":\"A theme.\",\"findings\":[{\"heading\":\"Finding\",\"analysis\":\"A sourced claim.\",\"source_ids\":[1]}],\"implications\":\"Consider a test.\",\"limitations\":\"One source.\"}";
            string result=Body.Contains("Write an analytical research brief",StringComparison.Ordinal)?synthesis:AnalysisJson;
            string output=JsonSerializer.Serialize(new{status="completed",output=new[]{new{type="message",content=new[]{new{type="output_text",text=result}}}}});
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(output,Encoding.UTF8,"application/json")};
        }
    }
}
