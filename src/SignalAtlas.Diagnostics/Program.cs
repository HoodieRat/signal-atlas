using System.Text.Json;
using SignalAtlas.Diagnostics;
using SignalAtlas.Data;

if(args.Contains("--seed-demo"))
{
    if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SIGNALATLAS_DATA_ROOT")))throw new InvalidOperationException("Demo seeding requires SIGNALATLAS_DATA_ROOT to protect normal user data.");
    var db=new ResearchDatabase();db.Initialize();
    if(!db.Topics().Any(x=>x.Name=="procedural animation")){long id=db.AddTopic("procedural animation");db.AddTerm(id,"procedural animation","include");}
    Console.WriteLine("Demo topic seeded in isolated data root.");return 0;
}
if(args.Contains("--test-config"))
{
    if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SIGNALATLAS_DATA_ROOT")))throw new InvalidOperationException("Test configuration requires SIGNALATLAS_DATA_ROOT.");
    var db=new ResearchDatabase();db.Initialize();db.SetSetting("selected_model","qwen/qwen3-1.7b");
    foreach(var topic in db.Topics())db.SetTopic(topic.Id,topic.Name,topic.Enabled,topic.Priority,topic.MaxAgeHours,2);
    foreach(var source in db.Sources().Where(x=>x.BaseUri is not null && x.BaseUri.Contains("linkedin.com")))db.SetSourceEnabled(source.Id,false);
    Console.WriteLine("Isolated test configuration applied.");return 0;
}
if(args.Contains("--test-scheduler"))
{
    if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SIGNALATLAS_DATA_ROOT")))throw new InvalidOperationException("Scheduler validation requires SIGNALATLAS_DATA_ROOT.");
    var db=new ResearchDatabase();db.Initialize();await new TaskSchedulerService(db,System.IO.Path.Combine(AppContext.BaseDirectory,"SignalAtlas.Runner.exe")).ValidateRegistrationAsync(CancellationToken.None);
    Console.WriteLine("Temporary Task Scheduler registration succeeded and was removed.");return 0;
}

if(args.Contains("--register-scheduler") || args.Contains("--remove-scheduler"))
{
    var db=new ResearchDatabase();db.Initialize();
    var scheduler=new TaskSchedulerService(db,System.IO.Path.Combine(AppContext.BaseDirectory,"SignalAtlas.Runner.exe"));
    if(args.Contains("--register-scheduler"))await scheduler.RegisterAsync(CancellationToken.None);
    else await scheduler.RemoveAsync(CancellationToken.None);
    return 0;
}

var results=await new DiagnosticService().RunAsync(args.Contains("--full"),CancellationToken.None);
foreach(var result in results)Console.WriteLine(JsonSerializer.Serialize(result));
return results.Any(x=>x.Essential && !x.Healthy)?1:0;
