using System.Diagnostics;
using System.Xml.Linq;
using SignalAtlas.Core;

namespace SignalAtlas.Data;

public sealed class TaskSchedulerService(ResearchDatabase database,string runnerPath):IScheduler
{
    public const string TaskName=@"\Signal Atlas\Scheduled Research";
    public static string TaskNameFor(long id)=>@"\Signal Atlas\Schedule "+id;
    private static readonly XNamespace Ns="http://schemas.microsoft.com/windows/2004/02/mit/task";
    public string BuildXml(IReadOnlyList<MonitorSchedule> schedules,long? scheduleId=null)
    {
        var active=schedules.Where(x=>x.Enabled).ToArray();
        var triggers=new XElement(Ns+"Triggers");
        foreach(var schedule in active)
        {
            var days=new XElement(Ns+"DaysOfWeek");
            foreach(var day in schedule.Days.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if(!Enum.TryParse<DayOfWeek>(day,true,out var parsed))throw new ArgumentException("Invalid day: "+day);
                days.Add(new XElement(Ns+parsed.ToString()));
            }
            if(!days.HasElements)continue;
            var time=TimeOnly.Parse(schedule.TimeOfDay);
            triggers.Add(new XElement(Ns+"CalendarTrigger",new XElement(Ns+"StartBoundary",DateTime.Today.Add(time.ToTimeSpan()).ToString("yyyy-MM-ddTHH:mm:ss")),new XElement(Ns+"Enabled","true"),new XElement(Ns+"ScheduleByWeek",days,new XElement(Ns+"WeeksInterval","1"))));
        }
        string user=Environment.UserDomainName+"\\"+Environment.UserName;
        var task=new XElement(Ns+"Task",new XAttribute("version","1.3"),new XElement(Ns+"RegistrationInfo",new XElement(Ns+"Description","Signal Atlas scheduled research")),triggers,
            new XElement(Ns+"Principals",new XElement(Ns+"Principal",new XAttribute("id","Author"),new XElement(Ns+"UserId",user),new XElement(Ns+"LogonType","InteractiveToken"),new XElement(Ns+"RunLevel","LeastPrivilege"))),
            new XElement(Ns+"Settings",new XElement(Ns+"MultipleInstancesPolicy","IgnoreNew"),new XElement(Ns+"StartWhenAvailable",active.Any(x=>x.StartWhenAvailable).ToString().ToLowerInvariant()),new XElement(Ns+"DisallowStartIfOnBatteries","false"),new XElement(Ns+"StopIfGoingOnBatteries","false"),new XElement(Ns+"AllowHardTerminate","true"),new XElement(Ns+"WakeToRun",active.Any(x=>x.WakeToRun).ToString().ToLowerInvariant()),new XElement(Ns+"ExecutionTimeLimit","PT"+Math.Clamp(active.FirstOrDefault()?.MaxRuntimeMinutes??45,1,1440)+"M"),new XElement(Ns+"RestartOnFailure",new XElement(Ns+"Interval","PT5M"),new XElement(Ns+"Count","2"))),
            new XElement(Ns+"Actions",new XAttribute("Context","Author"),new XElement(Ns+"Exec",new XElement(Ns+"Command",runnerPath),new XElement(Ns+"Arguments",scheduleId is null?"--scheduled":"--scheduled --schedule-id "+scheduleId))));
        return new XDocument(new XDeclaration("1.0","UTF-16",null),task).ToString();
    }
    public async Task RegisterAsync(CancellationToken cancellationToken)
    {
        var schedules=database.Schedules().Where(x=>x.Enabled).ToArray();if(!database.GetSetting("monitoring_enabled",false)||schedules.Length==0){await RemoveAsync(cancellationToken);return;}
        var old=database.GetSetting("registered_schedule_ids",Array.Empty<long>())!;
        foreach(var id in old.Except(schedules.Select(x=>x.Id)))await DeleteTaskAsync(TaskNameFor(id),cancellationToken);
        foreach(var schedule in schedules)
        {
            string file=Path.Combine(Path.GetTempPath(),"SignalAtlasTask-"+Guid.NewGuid().ToString("N")+".xml");
            try{File.WriteAllText(file,BuildXml([schedule],schedule.Id),System.Text.Encoding.Unicode);await RunAsync(["/Create","/TN",TaskNameFor(schedule.Id),"/XML",file,"/F"],cancellationToken);}
            finally{try{File.Delete(file);}catch{}}
        }
        await DeleteTaskAsync(TaskName,cancellationToken);
        database.SetSetting("registered_schedule_ids",schedules.Select(x=>x.Id).ToArray());
    }
    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        foreach(var id in database.GetSetting("registered_schedule_ids",Array.Empty<long>())!)await DeleteTaskAsync(TaskNameFor(id),cancellationToken);
        await DeleteTaskAsync(TaskName,cancellationToken);
        database.SetSetting("registered_schedule_ids",Array.Empty<long>());
    }
    private static async Task DeleteTaskAsync(string name,CancellationToken token){try{await RunAsync(["/Delete","/TN",name,"/F"],token);}catch(InvalidOperationException){}}
    public async Task ValidateRegistrationAsync(CancellationToken token)
    {
        string task=@"\Signal Atlas\Validation-"+Guid.NewGuid().ToString("N");
        string file=Path.Combine(Path.GetTempPath(),"SignalAtlasTaskValidation-"+Guid.NewGuid().ToString("N")+".xml");
        var schedule=new MonitorSchedule(0,"Validation",true,"08:00",["Monday"],true,false,45);
        try
        {
            File.WriteAllText(file,BuildXml([schedule],123),System.Text.Encoding.Unicode);
            await RunAsync(["/Create","/TN",task,"/XML",file,"/F"],token);
        }
        finally
        {
            try{await RunAsync(["/Delete","/TN",task,"/F"],CancellationToken.None);}catch{}
            try{File.Delete(file);}catch{}
        }
    }
    private static async Task RunAsync(string[] args,CancellationToken token)
    {
        var psi=new ProcessStartInfo("schtasks.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in args)psi.ArgumentList.Add(arg);
        using var process=Process.Start(psi)??throw new InvalidOperationException("Task Scheduler command failed to start");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try{var output=process.StandardOutput.ReadToEndAsync(deadline.Token);var error=process.StandardError.ReadToEndAsync(deadline.Token);await process.WaitForExitAsync(deadline.Token);if(process.ExitCode!=0)throw new InvalidOperationException(await error+await output);}
        catch(OperationCanceledException){try{process.Kill(true);}catch{}throw;}
    }
}
