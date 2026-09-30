using SignalAtlas.Worker;
using SignalAtlas.Core;

string? startGate=Option("--start-gate");
if(startGate is not null)
{
    AppPaths.Ensure();
    string expected=Path.GetFullPath(AppPaths.State)+Path.DirectorySeparatorChar;
    string full=Path.GetFullPath(startGate);
    if(!full.StartsWith(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid worker start gate");
    var deadline=DateTimeOffset.UtcNow.AddSeconds(10);
    while(!File.Exists(full) && DateTimeOffset.UtcNow<deadline)Thread.Sleep(100);
    if(!File.Exists(full))throw new TimeoutException("Runner did not release worker start gate");
    File.Delete(full);
}

string? existing=Option("--existing-run");
bool discoveryOnly=args.Contains("--discovery-only");
string trigger=Option("--trigger")??"manual";
using var cts=new CancellationTokenSource();
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cts.Cancel();};
long? scheduleId=long.TryParse(Option("--schedule-id"),out var sid)?sid:null;
try{await new RunCoordinator().RunAsync(existing,trigger,discoveryOnly,Option("--capture-file"),scheduleId,cts.Token);return 0;}
catch(OperationCanceledException){return 2;}
catch(Exception error){Console.Error.WriteLine(error);return 1;}
string? Option(string name){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
