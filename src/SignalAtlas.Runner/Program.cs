using System.Diagnostics;
using System.Text.Json;
using SignalAtlas.Core;
using SignalAtlas.Data;
using SignalAtlas.LmStudio;
using SignalAtlas.Resources;
using SignalAtlas.Runner;

AppPaths.Ensure();
using var mutex=new Mutex(false,@"Local\SignalAtlas.Run");
bool owned;
try{owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}
if(!owned){Console.Error.WriteLine("A research run is already active.");return 3;}
try{return SuperviseAsync(args).GetAwaiter().GetResult();}
finally{mutex.ReleaseMutex();}

static async Task<int> SuperviseAsync(string[] args)
{
    var db=new ResearchDatabase();db.Initialize();db.RecoverAbandoned();
    long? selectedSchedule=null;int scheduleArgument=Array.IndexOf(args,"--schedule-id");
    if(scheduleArgument>=0 && scheduleArgument+1<args.Length && long.TryParse(args[scheduleArgument+1],out long parsedId))selectedSchedule=parsedId;
    if(args.Contains("--scheduled"))
    {
        if(!db.GetSetting("monitoring_enabled",false))return 0;
        if(selectedSchedule is null || !db.Schedules().Any(x=>x.Id==selectedSchedule && x.Enabled))return 0;
    }
    var scheduleConfig=selectedSchedule is null?null:db.Schedules().FirstOrDefault(x=>x.Id==selectedSchedule);
    int maxRuntime=scheduleConfig?.MaxRuntimeMinutes??45;
    await new LmStudioBackend(new WindowsResourceProbe()).CleanupStaleOwnedAsync(CancellationToken.None);
    string worker=Path.Combine(AppContext.BaseDirectory,"SignalAtlas.Worker.exe");
    if(!File.Exists(worker))throw new FileNotFoundException("Worker executable not found",worker);
    var psi=new ProcessStartInfo(worker){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppContext.BaseDirectory};
    string gate=Path.Combine(AppPaths.State,"job-ready-"+Guid.NewGuid().ToString("N")+".flag");
    psi.ArgumentList.Add("--start-gate");psi.ArgumentList.Add(gate);
    psi.ArgumentList.Add("--trigger");psi.ArgumentList.Add(args.Contains("--scheduled")?"scheduled":"manual");
    if(args.Contains("--discovery-only") || scheduleConfig?.ReportMode=="discovery_only")psi.ArgumentList.Add("--discovery-only");
    int i=Array.IndexOf(args,"--existing-run");if(i>=0&&i+1<args.Length){psi.ArgumentList.Add("--existing-run");psi.ArgumentList.Add(args[i+1]);}
    int captureIndex=Array.IndexOf(args,"--capture-file");if(captureIndex>=0&&captureIndex+1<args.Length){psi.ArgumentList.Add("--capture-file");psi.ArgumentList.Add(args[captureIndex+1]);}
    int scheduleIndex=Array.IndexOf(args,"--schedule-id");if(scheduleIndex>=0&&scheduleIndex+1<args.Length){psi.ArgumentList.Add("--schedule-id");psi.ArgumentList.Add(args[scheduleIndex+1]);}
    using var process=Process.Start(psi)??throw new InvalidOperationException("Worker could not start");
    using var job=OwnedProcessJob.Attach(process);
    try{File.WriteAllText(gate,"ready");}catch{try{process.Kill(true);}catch{}throw;}
    try{process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch{}
    var started=DateTimeOffset.UtcNow;var lastBeat=started;string? runId=i>=0&&i+1<args.Length?args[i+1]:null;
    while(!process.HasExited)
    {
        await Task.Delay(5000);
        if(process.HasExited)break;
        var heartbeat=ReadHeartbeat();
        if(heartbeat is not null && heartbeat.Value.Pid==process.Id && heartbeat.Value.Utc>=started.AddSeconds(-1)){runId=heartbeat.Value.RunId;lastBeat=heartbeat.Value.Utc;}
        bool overTime=DateTimeOffset.UtcNow-started>TimeSpan.FromMinutes(Math.Clamp(maxRuntime,1,1440));
        bool stale=DateTimeOffset.UtcNow-lastBeat>TimeSpan.FromSeconds(120);
        if(!overTime&&!stale)continue;
        if(runId is not null)File.WriteAllText(Path.Combine(AppPaths.State,"cancel-"+runId+".flag"),"cancel");
        using(var grace=new CancellationTokenSource(TimeSpan.FromSeconds(15))){try{await process.WaitForExitAsync(grace.Token);}catch(OperationCanceledException){}}
        if(!process.HasExited)process.Kill(entireProcessTree:true);
        await new LmStudioBackend(new WindowsResourceProbe()).CleanupStaleOwnedAsync(CancellationToken.None);
        runId??=db.ActiveRunForPid(process.Id);
        if(runId is not null && db.GetRun(runId)?.Status=="active")db.Finish(runId,"PARTIAL",overTime?"Maximum runtime exceeded":"Worker heartbeat stopped");
        return 4;
    }
    runId??=db.ActiveRunForPid(process.Id);
    if(process.ExitCode!=0)
    {
        if(runId is not null && db.GetRun(runId)?.Status=="active")db.Finish(runId,"PARTIAL","Worker exited unexpectedly");
        await new LmStudioBackend(new WindowsResourceProbe()).CleanupStaleOwnedAsync(CancellationToken.None);
    }
    return process.ExitCode;
}
static (string RunId,int Pid,DateTimeOffset Utc)? ReadHeartbeat()
{
    try{using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppPaths.State,"heartbeat.json")));var root=json.RootElement;return(root.GetProperty("runId").GetString()??"",root.GetProperty("pid").GetInt32(),root.GetProperty("utc").GetDateTimeOffset());}catch{return null;}
}
