using SignalAtlas.Collectors;
using SignalAtlas.Core;
using SignalAtlas.Data;
using SignalAtlas.LmStudio;
using SignalAtlas.Resources;

namespace SignalAtlas.Diagnostics;

public sealed record DiagnosticResult(string Component,bool Healthy,bool Essential,string Message);

public sealed class DiagnosticService
{
    public async Task<IReadOnlyList<DiagnosticResult>> RunAsync(bool full,CancellationToken token)
    {
        var list=new List<DiagnosticResult>();
        void Add(string name,bool good,bool essential,string message)=>list.Add(new(name,good,essential,message));
        try{AppPaths.Ensure();var db=new ResearchDatabase();db.Initialize();using var c=db.Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA integrity_check";Add("Database",cmd.ExecuteScalar()?.ToString()=="ok",true,"SQLite integrity check");}
        catch(Exception e){Add("Database",false,true,e.Message);}
        var doko=ProcessTool.FindDokoBot();Add("DokoBot CLI",doko is not null,false,doko is null?"Not installed":"Found");
        var lms=ProcessTool.FindExecutable("lms");Add("LM Studio CLI",lms is not null,false,lms is null?"Not installed":"Found");
        var probe=new WindowsResourceProbe();
        try{var s=probe.Sample();Add("RAM",s.AvailableRamBytes>0,true,$"{s.AvailableRamBytes/1024d/1024/1024:0.0} GiB available");Add("GPU budget",s.GpuBudgetBytes is >0,false,s.GpuBudgetBytes is >0?$"{s.GpuBudgetBytes/1024d/1024/1024:0.0} GiB safety budget; {s.GpuUsageBytes/1024d/1024/1024:0.0} GiB currently used":"DXGI budget unavailable; AI will defer");Add("Disk",s.FreeDiskBytes>=2L*1024*1024*1024,true,$"{s.FreeDiskBytes/1024d/1024/1024:0.0} GiB free");}
        catch(Exception e){Add("Resources",false,true,e.Message);}
        var cpu=new WindowsCpuProbe();cpu.Sample();await Task.Delay(300,token);double? cpuPercent=cpu.Sample();Add("CPU",cpuPercent is not null,false,cpuPercent is null?"System CPU sample unavailable":$"{cpuPercent:0.0}% system use");
        Add("Reports",Directory.Exists(AppPaths.Reports),true,AppPaths.Reports);
        if(full)
        {
            foreach(var (name,exe) in new[]{("Edge",@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),("Chrome",@"C:\Program Files\Google\Chrome\Application\chrome.exe"),("Brave",@"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe")})Add(name,File.Exists(exe),false,File.Exists(exe)?"Installed":"Not found");
            if(doko is not null)
            {
                try{using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(20));string content=await new WebCollector().CaptureChosenPageAsync("https://example.com",false,deadline.Token);Add("DokoBot bridge",content.Length>=40,false,"Local browser read succeeded");}
                catch(Exception e) when(e is not OperationCanceledException || !token.IsCancellationRequested){Add("DokoBot bridge",false,false,"Local browser read failed: "+e.Message);}
            }
            if(lms is not null)
            {
                try{var models=await new LmStudioBackend(probe).InstalledAsync(token);Add("Model inventory",models.Count>0,false,$"{models.Count} local LLMs");}
                catch(Exception e){Add("Model inventory",false,false,e.Message);}
            }
        }
        return list;
    }
}
