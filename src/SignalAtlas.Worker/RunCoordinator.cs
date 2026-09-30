using System.Text.Json;
using SignalAtlas.Collectors;
using SignalAtlas.Core;
using SignalAtlas.Data;
using SignalAtlas.LmStudio;
using SignalAtlas.OpenAI;
using SignalAtlas.Reporting;
using SignalAtlas.Resources;

namespace SignalAtlas.Worker;

public sealed class RunCoordinator
{
    private readonly ResearchDatabase _db=new();
    private readonly WebCollector _collector=new();
    private IModelBackend _model=new LmStudioBackend(new WindowsResourceProbe());
    public async Task RunAsync(string? existingRun,string trigger,bool discoveryOnly,string? captureFile,long? scheduleId,CancellationToken token)
    {
        AppPaths.Ensure();_db.Initialize();_db.RecoverAbandoned();
        var reportOptions=_db.GetSetting("report_options",new ReportOptions())??new ReportOptions();reportOptions.Validate();
        if(discoveryOnly)reportOptions=reportOptions with{Output="cards"};
        _db.SetSetting("report_progress","");
        _collector.BlockLinkedInReads=_db.GetSetting("block_linkedin_dokobot_reads",true);
        string provider=_db.GetSetting("ai_provider","lm_studio")!;
        _model=provider switch
        {
            "openai_api" => new OpenAiApiBackend(),
            "codex_chatgpt" => new CodexCliBackend(),
            _ => new LmStudioBackend(new WindowsResourceProbe())
            {
                PreferredContext=_db.GetSetting("preferred_context",8192),
                MinimumContext=_db.GetSetting("minimum_context",4096),
                RestrictBattery=_db.GetSetting("restrict_battery",true)
            }
        };
        var cpuProbe=new WindowsCpuProbe();cpuProbe.Sample();
        string id=existingRun??Guid.NewGuid().ToString("N");
        if(existingRun is null)_db.CreateRun(id,trigger,scheduleId);
        else if(_db.GetRun(id)?.Status!="active")throw new InvalidOperationException("Capture run is missing or inactive");
        if(existingRun is null && !discoveryOnly)_db.RecoverPendingToRun(id);
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(token);
        using var heartbeat=StartHeartbeat(id,stop);
        bool partial=false;string? deferred=null;ResearchBrief? synthesis=null;bool modelReady=false;
        try
        {
            Phase("PRECHECK");
            if(new WindowsResourceProbe().Sample().FreeDiskBytes<2L*1024*1024*1024)throw new InvalidOperationException("Less than 2 GiB free disk");
            Phase("COLLECTING");
            if(captureFile is not null)
            {
                string full=Path.GetFullPath(captureFile);
                if(!full.StartsWith(Path.GetFullPath(AppPaths.State)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||new FileInfo(full).Length>1_000_000)throw new InvalidDataException("Invalid capture request file");
                var capture=JsonSerializer.Deserialize<CaptureManifest>(File.ReadAllText(full))??throw new InvalidDataException("Invalid capture request");
                if(!_db.Topics().Any(x=>x.Id==capture.TopicId))throw new InvalidDataException("Unknown capture topic");
                if(capture.Urls.Length==0)throw new InvalidDataException("No capture URLs");
                foreach(var url in capture.Urls.Distinct())
                {
                    stop.Token.ThrowIfCancellationRequested();
                    if(capture.UseDokoBot && _db.GetSetting("block_linkedin_dokobot_reads",true))
                    {
                        partial=true;Log(id,"Warning","COLLECTING","LINKEDIN_BLOCKED","LinkedIn DokoBot reads were blocked before the next page");break;
                    }
                    if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https") || capture.RequireLinkedIn && !SourcePolicy.IsLinkedIn(uri))throw new InvalidDataException("Capture contains a disallowed URL");
                    try
                    {
                        string content;
                        if(capture.UseDokoBot)
                        {
                            if(_collector.BlockLinkedInReads)throw new InvalidOperationException("LinkedIn DokoBot reads are blocked in settings");
                            content=await _collector.CaptureChosenPageAsync(url,false,stop.Token);
                        }
                        else content=capture.Text??throw new InvalidDataException("Copied text is missing");
                        _db.ImportChosenContent(capture.TopicId,url,capture.Title,content,id,capture.RequireLinkedIn);
                    }
                    catch(Exception e) when(capture.UseDokoBot && e is not OperationCanceledException){partial=true;_db.Increment(id,"failed_count");Log(id,"Warning","COLLECTING","CAPTURE_FAILED",e.Message);}
                    if(!capture.UseDokoBot)break;
                }
                File.Delete(full);
            }
            else if(existingRun is null)
            {
                var schedule=scheduleId is null?null:_db.Schedules().FirstOrDefault(x=>x.Id==scheduleId);
                foreach(var topic in _db.Topics(true).Where(x=>schedule?.TopicIds is not {Length:>0} || schedule.TopicIds.Contains(x.Id)))
                foreach(var source in _db.Sources(true))
                {
                    stop.Token.ThrowIfCancellationRequested();
                    if(source.Type is "manual" or "linkedin_capture" || !SourcePolicy.IsValid(source) || !_db.IsSourceEnabledForTopic(topic.Id,source.Id))continue;
                    try
                    {
                        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);deadline.CancelAfter(TimeSpan.FromSeconds(60));
                        foreach(var d in await _collector.DiscoverAsync(topic,source,deadline.Token))
                        {
                            stop.Token.ThrowIfCancellationRequested();
                            long discoveryId=_db.AddDiscovery(id,d);_db.Increment(id,"discovered_count");
                            bool newlyBlocked=Uri.TryCreate(d.CanonicalUrl,UriKind.Absolute,out var resultUri) && SourcePolicy.IsLinkedIn(resultUri) && _db.GetSetting("block_linkedin_dokobot_reads",true);
                            if(discoveryOnly || !d.FetchAllowed || newlyBlocked)
                            {
                                var (snippetDoc,_) = _db.UpsertDocument(d,d.Title,d.Snippet.Length>0?d.Snippet:d.Title);
                                _db.SetDiscoveryDocument(discoveryId,snippetDoc);_db.LinkDocument(snippetDoc,topic.Id,d.Score);_db.SetDiscoveryStatus(discoveryId,"discovery_only");continue;
                            }
                            string? text=null;
                            try
                            {
                                using var fetchDeadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);fetchDeadline.CancelAfter(TimeSpan.FromSeconds(100));
                                text=await _collector.FetchAsync(d,fetchDeadline.Token);
                            }
                            catch(Exception e) when(e is not OperationCanceledException || !stop.IsCancellationRequested){Log(id,"Warning","COLLECTING","FETCH_FAILED",e.Message);}
                            if(string.IsNullOrWhiteSpace(text)){_db.SetDiscoveryStatus(discoveryId,"failed");_db.Increment(id,"failed_count");partial=true;continue;}
                            _db.SetDiscoveryStatus(discoveryId,"fetched");_db.Increment(id,"fetched_count");
                            var (docId,isNew)=_db.UpsertDocument(d,d.Title,text);_db.SetDiscoveryDocument(discoveryId,docId);_db.LinkDocument(docId,topic.Id,d.Score);
                            if(isNew && text.Length>=250 && d.Score>=.2 && ContentTools.Score(topic,text[..Math.Min(text.Length,2000)],"")>=.2)_db.Queue(id,docId);
                            else if(!isNew)_db.Increment(id,"duplicate_count");
                        }
                    }
                    catch(Exception e) when(e is not OperationCanceledException || !stop.IsCancellationRequested){_db.Increment(id,"failed_count");partial=true;Log(id,"Warning","COLLECTING","SOURCE_FAILED",source.Name+": "+e.Message);}
                }
            }
            Phase("NORMALIZING");Phase("DEDUPLICATING");Phase("MODEL_PREP");
            var pending=_db.Pending(id);
            if(!discoveryOnly && (pending.Count>0 || reportOptions.WantsReport && _db.ReportItems(id).Any(x=>x.Analysis is not null)))
            {
                string selected=provider switch
                {
                    "openai_api" => _db.GetSetting("openai_model","gpt-6-luna")!,
                    "codex_chatgpt" => "codex-chatgpt",
                    _ => _db.GetSetting("selected_model", "qwen/qwen3-4b-2507")!
                };
                if(_model is LmStudioBackend local && await SustainedCpuPressureAsync(id,cpuProbe,stop.Token))local.PreferredContext=local.MinimumContext;
                try{modelReady=await _model.PrepareAsync(selected,stop.Token);if(!modelReady){deferred=_model.DeferredReason;partial=true;}}
                catch(Exception e) when(e is not OperationCanceledException){deferred="Model unavailable: "+e.Message;partial=true;Log(id,"Warning","MODEL_PREP","MODEL_UNAVAILABLE",e.Message);}
            }
            Phase("ANALYZING");var analyses=new List<Analysis>();int failures=0;
            if(modelReady)
            foreach(var (queueId,document) in pending)
            {
                stop.Token.ThrowIfCancellationRequested();
                var topic=_db.Topics().First(x=>x.Id==document.TopicId);
                try
                {
                    Analysis? result=null;
                    for(int attempt=0;attempt<2 && result is null;attempt++)
                    {
                        try{using var request=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);request.CancelAfter(TimeSpan.FromSeconds(120));var input=attempt==0?document:document with{Text=document.Text[..Math.Min(document.Text.Length,6000)]};result=await _model.AnalyzeAsync(input,topic,request.Token);}
                        catch(Exception e) when(attempt==0 && !stop.IsCancellationRequested && e is not ResourceEmergencyException){Log(id,"Warning","ANALYZING","RETRY",e.Message);}
                    }
                    if(result is null)throw new InvalidDataException("Empty analysis result");
                    _db.SaveAnalysis(result);_db.CompleteQueue(queueId,"completed");_db.Increment(id,"analyzed_count");analyses.Add(result);
                }
                catch(ResourceEmergencyException e){_db.CompleteQueue(queueId,"pending",e.Message);partial=true;deferred="AI stopped because system resources became unsafe";Log(id,"Warning","ANALYZING","RESOURCE_EMERGENCY",e.Message);await _model.CleanupAsync(CancellationToken.None);break;}
                catch(Exception e) when(e is not OperationCanceledException || !stop.IsCancellationRequested){_db.CompleteQueue(queueId,"failed",e.Message);_db.Increment(id,"failed_count");partial=true;failures++;Log(id,"Warning","ANALYZING","ANALYSIS_FAILED",e.Message);if(failures>=3){deferred="AI circuit breaker opened after three failures";break;}}
            }
            Phase("SYNTHESIZING");
            var reportItems=_db.ReportItems(id,loadText:reportOptions.WantsReport);
            ResearchReport? report=null;RenderedPdf? pdf=null;string? reportNotice=null;
            if(reportOptions.WantsReport && modelReady && reportItems.Any(x=>x.Analysis is not null))
            {
                try
                {
                    var composer=new ReportComposer(_model);
                    report=await composer.ComposeAsync(reportItems,reportOptions,message=>{_db.SetSetting("report_progress",message);Log(id,"Information","SYNTHESIZING","REPORT_PROGRESS",message);},stop.Token);
                    for(int attempt=0;attempt<3;attempt++)
                    {
                        _db.SetSetting("report_progress","Laying out and checking report pages");
                        pdf=new PdfReportRenderer().Render(report);
                        if(pdf.Pages<=reportOptions.Pages)break;
                        if(attempt==2)throw new InvalidDataException($"The report still needs {pdf.Pages} pages, exceeding the selected {reportOptions.Pages}-page limit. Select a longer report or fewer optional sections.");
                        report=await composer.ShortenAsync(report,Math.Min(.8,(double)reportOptions.Pages/pdf.Pages*.8),stop.Token);
                    }
                    synthesis=new ResearchBrief(string.Join("\n\n",report.ExecutiveSummary.Select(x=>x.Text)),[],"","");
                }
                catch(Exception e) when(e is not OperationCanceledException || !stop.IsCancellationRequested)
                {
                    report=null;pdf=null;partial=true;reportNotice="The research report could not be completed: "+e.Message+" Your card digest is available below.";
                    Log(id,"Warning","SYNTHESIZING","REPORT_FAILED",e.Message);
                }
            }
            else if(reportOptions.WantsReport){reportNotice="The research report requires analyzed sources. Your card digest is available below; configure an AI provider and run research to write the report.";if(reportItems.Count>0)partial=true;}
            if(report is null && analyses.Count>0 && modelReady)
            {
                try{using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);deadline.CancelAfter(TimeSpan.FromSeconds(180));synthesis=await _model.SynthesizeAsync(reportItems,deadline.Token);}
                catch(Exception e) when(e is not OperationCanceledException || !stop.IsCancellationRequested){partial=true;Log(id,"Warning","SYNTHESIZING","SYNTHESIS_FAILED",e.Message);}
            }
            Phase("PUBLISHING");string status=partial?"PARTIAL":"COMPLETED";
            var run=_db.GetRun(id)! with{Status=status,DeferredReason=deferred};
            if(discoveryOnly)reportNotice="Discovery-only run. These cards contain source links and context; AI report writing was not requested.";
            var files=HtmlReportRenderer.PublishBundle(run,reportItems,synthesis,report,pdf,reportOptions,reportNotice);
            _db.SaveReport(id,files.PrimaryHtml,synthesis?.ExecutiveSummary,partial,files);_db.SetSetting("report_progress","");Phase("CLEANUP");await _model.CleanupAsync(CancellationToken.None);_db.Finish(id,status,deferred);
            try{new RetentionService(_db).Apply(_db.GetSetting("reports_retention_days",180),_db.GetSetting("content_retention_days",30),_db.GetSetting("cache_retention_days",7),_db.GetSetting("logs_retention_days",30));}catch(Exception e){Log(id,"Warning","CLEANUP","RETENTION_FAILED",e.Message);}
        }
        catch(OperationCanceledException){try{await _model.CleanupAsync(CancellationToken.None);}catch{} _db.Finish(id,"CANCELLED","User or supervisor cancelled");throw;}
        catch(Exception e){try{await _model.CleanupAsync(CancellationToken.None);}catch{}try{_db.Finish(id,"FAILED",null,e.Message);Log(id,"Error","CLEANUP","RUN_FAILED",e.Message);}catch{}throw;}
        finally{stop.Cancel();try{_db.SetSetting("report_progress","");File.Delete(CancelFile(id));}catch{}}
        void Phase(string name){_db.Phase(id,name);Log(id,"Information",name,"PHASE_START",name);}
    }
    private IDisposable StartHeartbeat(string id,CancellationTokenSource stop)
    {
        var task=Task.Run(async()=>{while(!stop.IsCancellationRequested){try{_db.Heartbeat(id);File.WriteAllText(Path.Combine(AppPaths.State,"heartbeat.json"),JsonSerializer.Serialize(new{runId=id,pid=Environment.ProcessId,utc=DateTimeOffset.UtcNow}));if(File.Exists(CancelFile(id)))stop.Cancel();await Task.Delay(5000,stop.Token);}catch(OperationCanceledException){break;}catch{await Task.Delay(5000);}}});
        return new HeartbeatHandle(stop,task);
    }
    private async Task<bool> SustainedCpuPressureAsync(string runId,WindowsCpuProbe probe,CancellationToken token)
    {
        double? first=probe.Sample();if(first is null or <=85)return false;
        var samples=new List<double>{first.Value};
        for(int i=0;i<6;i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5),token);
            double? usage=probe.Sample();if(usage is null)return false;samples.Add(usage.Value);
        }
        if(samples.Average()<=85)return false;
        Log(runId,"Warning","MODEL_PREP","CPU_PRESSURE","CPU averaged above 85% for 30 seconds; waiting for relief");
        for(int i=0;i<12;i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5),token);
            if(probe.Sample() is <=85)return false;
        }
        Log(runId,"Warning","MODEL_PREP","CPU_REDUCED","CPU pressure persisted; using minimum model context");
        return true;
    }
    private sealed class HeartbeatHandle(CancellationTokenSource stop,Task task):IDisposable{public void Dispose(){stop.Cancel();try{task.Wait(TimeSpan.FromSeconds(2));}catch{}}}
    public static string CancelFile(string id)=>Path.Combine(AppPaths.State,"cancel-"+id+".flag");
    private void Log(string run,string severity,string phase,string code,string message)
    {
        string safe=message.Length>500?message[..500]:message;_db.Event(run,severity,phase,code,safe);
        string line=JsonSerializer.Serialize(new{timestamp=DateTimeOffset.UtcNow,run_id=run,component="worker",phase,event_code=code,level=severity,message=safe});
        File.AppendAllText(Path.Combine(AppPaths.Logs,"worker-"+DateTimeOffset.UtcNow.ToString("yyyy-MM-dd")+".jsonl"),line+Environment.NewLine);
    }
}
