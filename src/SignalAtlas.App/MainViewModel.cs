using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SignalAtlas.Collectors;
using SignalAtlas.Core;
using SignalAtlas.Data;
using SignalAtlas.Diagnostics;
using SignalAtlas.LmStudio;
using SignalAtlas.OpenAI;
using SignalAtlas.Resources;

namespace SignalAtlas.App;

public sealed class AsyncCommand(Func<Task> action):ICommand
{
    private bool _busy;public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter)=>!_busy;
    public async void Execute(object? parameter){if(_busy)return;_busy=true;CanExecuteChanged?.Invoke(this,EventArgs.Empty);try{await action();}catch(Exception e){System.Windows.MessageBox.Show(e.Message,"Signal Atlas",System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Warning);}finally{_busy=false;CanExecuteChanged?.Invoke(this,EventArgs.Empty);}}
}
public sealed class SourceSelection(long id,string name,bool enabled):INotifyPropertyChanged
{
    private bool _enabled=enabled;public long Id=>id;public string Name=>name;
    public bool Enabled{get=>_enabled;set{_enabled=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Enabled)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class MainViewModel:INotifyPropertyChanged
{
    private readonly ResearchDatabase _db=new();
    private readonly WebCollector _collector=new();
    private string _status="Ready";
    private int _mainTabIndex;
    private int _setupStep;
    private string _topicName="";
    private string _topicTerms="";
    private string _topicExact="";
    private string _topicExclude="";
    private string _topicSynonyms="";
    private string _topicAll="";
    private string _topicDomains="";
    private int _topicPriority=50;
    private int _topicMaxAge=168;
    private int _topicMaxResults=30;
    private bool _topicEnabled=true;
    private string _sourceName="";
    private string _sourceUrl="";
    private string _sourceType="rss";
    private string _captureUrls="";
    private string _captureText="";
    private string _captureTitle="LinkedIn post";
    private string _scheduleName="Daily research";
    private string _scheduleTime="08:00";
    private string _scheduleDays="Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday";
    private bool _scheduleEnabled=true;
    private bool _scheduleWake;
    private bool _scheduleStartMissed=true;
    private int _scheduleMaxRuntime=45;
    private string _scheduleReportMode="normal";
    private Topic? _selectedTopic;
    private Source? _selectedSource;
    private MonitorSchedule? _selectedSchedule;
    private InstalledModel? _selectedModel;
    private string? _selectedReport;
    private BookmarkEntry? _selectedReportEntry;
    private bool _blockLinkedInReads;
    private string _aiProvider="lm_studio";
    private string _openAiModel="gpt-6-luna";
    private bool _monitoringEnabled;
    private int _preferredContext;
    private int _minimumContext;
    private bool _restrictBattery;
    private int _reportsRetentionDays;
    private int _contentRetentionDays;
    private int _cacheRetentionDays;
    private int _logsRetentionDays;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    public ObservableCollection<Topic> Topics{get;}=[];
    public ObservableCollection<Source> Sources{get;}=[];
    public ObservableCollection<SourceSelection> TopicSources{get;}=[];
    public ObservableCollection<MonitorSchedule> Schedules{get;}=[];
    public ObservableCollection<SourceSelection> ScheduleTopics{get;}=[];
    public ObservableCollection<InstalledModel> Models{get;}=[];
    public ObservableCollection<string> Reports{get;}=[];
    public ObservableCollection<BookmarkEntry> ReportEntries{get;}=[];
    public ObservableCollection<DiagnosticResult> Diagnostics{get;}=[];
    public string Status{get=>_status;set{_status=value;Changed();}}
    public int MainTabIndex{get=>_mainTabIndex;set{_mainTabIndex=value;Changed();}}
    public int SetupStep{get=>_setupStep;set{_setupStep=Math.Clamp(value,0,5);Changed();}}
    public string TopicName{get=>_topicName;set{_topicName=value;Changed();}}
    public string TopicTerms{get=>_topicTerms;set{_topicTerms=value;Changed();}}
    public string TopicExact{get=>_topicExact;set{_topicExact=value;Changed();}}
    public string TopicExclude{get=>_topicExclude;set{_topicExclude=value;Changed();}}
    public string TopicSynonyms{get=>_topicSynonyms;set{_topicSynonyms=value;Changed();}}
    public string TopicAll{get=>_topicAll;set{_topicAll=value;Changed();}}
    public string TopicDomains{get=>_topicDomains;set{_topicDomains=value;Changed();}}
    public int TopicPriority{get=>_topicPriority;set{_topicPriority=value;Changed();}}
    public int TopicMaxAge{get=>_topicMaxAge;set{_topicMaxAge=value;Changed();}}
    public int TopicMaxResults{get=>_topicMaxResults;set{_topicMaxResults=value;Changed();}}
    public bool TopicEnabled{get=>_topicEnabled;set{_topicEnabled=value;Changed();}}
    public string SourceName{get=>_sourceName;set{_sourceName=value;Changed();}}
    public string SourceUrl{get=>_sourceUrl;set{_sourceUrl=value;Changed();}}
    public string SourceType{get=>_sourceType;set{_sourceType=value;Changed();}}
    public string CaptureUrls{get=>_captureUrls;set{_captureUrls=value;Changed();}}
    public string CaptureText{get=>_captureText;set{_captureText=value;Changed();}}
    public string CaptureTitle{get=>_captureTitle;set{_captureTitle=value;Changed();}}
    public string ScheduleName{get=>_scheduleName;set{_scheduleName=value;Changed();}}
    public string ScheduleTime{get=>_scheduleTime;set{_scheduleTime=value;Changed();}}
    public string ScheduleDays{get=>_scheduleDays;set{_scheduleDays=value;Changed();}}
    public bool ScheduleEnabled{get=>_scheduleEnabled;set{_scheduleEnabled=value;Changed();}}
    public bool ScheduleWake{get=>_scheduleWake;set{_scheduleWake=value;Changed();}}
    public bool ScheduleStartMissed{get=>_scheduleStartMissed;set{_scheduleStartMissed=value;Changed();}}
    public int ScheduleMaxRuntime{get=>_scheduleMaxRuntime;set{_scheduleMaxRuntime=value;Changed();}}
    public string ScheduleReportMode{get=>_scheduleReportMode;set{_scheduleReportMode=value;Changed();}}
    public Topic? SelectedTopic{get=>_selectedTopic;set{_selectedTopic=value;Changed();if(value is not null){TopicName=value.Name;TopicTerms=Terms(value,"include");TopicExact=Terms(value,"exact");TopicExclude=Terms(value,"exclude");TopicSynonyms=Terms(value,"synonym");TopicAll=string.Join(", ",value.AllKeywords??[]);TopicDomains=string.Join(", ",value.Domains??[]);TopicPriority=value.Priority;TopicMaxAge=value.MaxAgeHours;TopicMaxResults=value.MaxResultsPerRun;TopicEnabled=value.Enabled;var selections=_db.TopicSourceSelections(value.Id);Refill(TopicSources,_db.Sources().Where(x=>x.Type is not("manual" or "linkedin_capture")).Select(x=>new SourceSelection(x.Id,x.Name,selections.Count==0 || selections.GetValueOrDefault(x.Id))));}else TopicSources.Clear();}}
    private static string Terms(Topic topic,string type)=>string.Join(", ",topic.Terms.Where(x=>x.Type==type).Select(x=>x.Term));
    public Source? SelectedSource{get=>_selectedSource;set{_selectedSource=value;Changed();}}
    public MonitorSchedule? SelectedSchedule{get=>_selectedSchedule;set{_selectedSchedule=value;Changed();if(value is not null){ScheduleName=value.Name;ScheduleTime=value.TimeOfDay;ScheduleDays=string.Join(",",value.Days);ScheduleEnabled=value.Enabled;ScheduleWake=value.WakeToRun;ScheduleStartMissed=value.StartWhenAvailable;ScheduleMaxRuntime=value.MaxRuntimeMinutes;ScheduleReportMode=value.ReportMode;Refill(ScheduleTopics,_db.Topics().Select(x=>new SourceSelection(x.Id,x.Name,value.TopicIds is not {Length:>0}||value.TopicIds.Contains(x.Id))));}}}
    public InstalledModel? SelectedModel{get=>_selectedModel;set{_selectedModel=value;Changed();}}
    public string? SelectedReport{get=>_selectedReport;set{_selectedReport=value;Changed();string? path=value?.Split(" — ",2).LastOrDefault();Refill(ReportEntries,path is not null && File.Exists(path)?_db.ReportEntries(path):[]);Changed(nameof(HasCards));Changed(nameof(HasResearchReport));Changed(nameof(HasPdf));}}
    public BookmarkEntry? SelectedReportEntry{get=>_selectedReportEntry;set{_selectedReportEntry=value;Changed();}}
    public bool BlockLinkedInReads{get=>_blockLinkedInReads;set{_blockLinkedInReads=value;_db.SetSetting("block_linkedin_dokobot_reads",value);Changed();Changed(nameof(LinkedInPolicyStatus));Status=value?"LinkedIn DokoBot reads blocked in Signal Atlas":"LinkedIn DokoBot reads allowed in Signal Atlas";}}
    public string AiProvider{get=>_aiProvider;set{if(value is not("lm_studio" or "openai_api" or "codex_chatgpt"))return;_aiProvider=value;_db.SetSetting("ai_provider",value);Changed();Status="AI provider selected: "+value;}}
    public string OpenAiModel{get=>_openAiModel;set{_openAiModel=value;Changed();}}
    public string LinkedInPolicyStatus=>BlockLinkedInReads?"Current policy: indexed LinkedIn links only; DokoBot page reads blocked.":"Current policy: scheduled and chosen multi-page LinkedIn DokoBot reads allowed.";
    public bool MonitoringEnabled{get=>_monitoringEnabled;set{_monitoringEnabled=value;Changed();}}
    public int PreferredContext{get=>_preferredContext;set{_preferredContext=value;Changed();}}
    public int MinimumContext{get=>_minimumContext;set{_minimumContext=value;Changed();}}
    public bool RestrictBattery{get=>_restrictBattery;set{_restrictBattery=value;Changed();}}
    public int ReportsRetentionDays{get=>_reportsRetentionDays;set{_reportsRetentionDays=value;Changed();}}
    public int ContentRetentionDays{get=>_contentRetentionDays;set{_contentRetentionDays=value;Changed();}}
    public int CacheRetentionDays{get=>_cacheRetentionDays;set{_cacheRetentionDays=value;Changed();}}
    public int LogsRetentionDays{get=>_logsRetentionDays;set{_logsRetentionDays=value;Changed();}}
    public string LatestRun{get{var run=_db.LatestRun();return run is null?"No runs yet":$"{run.Status} · {run.Phase} · {run.Discovered} discovered · {run.Analyzed} analyzed";}}
    public string SelectedModelKey=>_db.GetSetting("selected_model","qwen/qwen3-4b-2507")!;
    public ICommand RefreshCommand{get;}
    public ICommand AddTopicCommand{get;}
    public ICommand SaveTopicCommand{get;}
    public ICommand ToggleSourceCommand{get;}
    public ICommand AddSourceCommand{get;}
    public ICommand AddScheduleCommand{get;}
    public ICommand NewScheduleCommand{get;}
    public ICommand DeleteScheduleCommand{get;}
    public ICommand EnableMonitoringCommand{get;}
    public ICommand PauseMonitoringCommand{get;}
    public ICommand RunNowCommand{get;}
    public ICommand DiscoveryOnlyCommand{get;}
    public ICommand StopCommand{get;}
    public ICommand LoadModelsCommand{get;}
    public ICommand SelectModelCommand{get;}
    public ICommand TestModelCommand{get;}
    public ICommand SaveAiSettingsCommand{get;}
    public ICommand CheckCodexCommand{get;}
    public ICommand SignInCodexCommand{get;}
    public ICommand CheckApiCommand{get;}
    public ICommand InstallFastCommand{get;}
    public ICommand InstallQualityCommand{get;}
    public ICommand OpenReportCommand{get;}
    public ICommand OpenReportsFolderCommand{get;}
    public ICommand CopySummaryCommand{get;}
    public ICommand ExportReportCommand{get;}
    public ICommand DeleteReportCommand{get;}
    public ICommand ToggleBookmarkCommand{get;}
    public ICommand RunDiagnosticsCommand{get;}
    public ICommand CaptureCopiedCommand{get;}
    public ICommand CaptureDokoBatchCommand{get;}
    public ICommand ExampleTopicsCommand{get;}
    public ICommand InstallBridgeCommand{get;}
    public ICommand InstallDokoCommand{get;}
    public ICommand OpenLmStudioInstallCommand{get;}
    public ICommand SaveSettingsCommand{get;}
    public ICommand SetupNextCommand{get;}
    public ICommand SetupBackCommand{get;}
    public ICommand AdvancedSetupCommand{get;}
    public ICommand OpenTopicsCommand{get;}
    public ICommand OpenScheduleCommand{get;}
    public ICommand OpenReportsCommand{get;}
    public ICommand OpenAiSettingsCommand{get;}
    public MainViewModel()
    {
        AppPaths.Ensure();_db.Initialize();_blockLinkedInReads=_db.GetSetting("block_linkedin_dokobot_reads",true);_monitoringEnabled=_db.GetSetting("monitoring_enabled",false);
        _aiProvider=_db.GetSetting("ai_provider","lm_studio")!;_openAiModel=_db.GetSetting("openai_model","gpt-6-luna")!;
        _preferredContext=_db.GetSetting("preferred_context",8192);_minimumContext=_db.GetSetting("minimum_context",4096);_restrictBattery=_db.GetSetting("restrict_battery",true);
        _reportsRetentionDays=_db.GetSetting("reports_retention_days",180);_contentRetentionDays=_db.GetSetting("content_retention_days",30);_cacheRetentionDays=_db.GetSetting("cache_retention_days",7);_logsRetentionDays=_db.GetSetting("logs_retention_days",30);
        RefreshCommand=new AsyncCommand(()=>{Refresh();return Task.CompletedTask;});
        AddTopicCommand=new AsyncCommand(AddTopicAsync);SaveTopicCommand=new AsyncCommand(SaveTopicAsync);
        ToggleSourceCommand=new AsyncCommand(ToggleSourceAsync);AddSourceCommand=new AsyncCommand(AddSourceAsync);AddScheduleCommand=new AsyncCommand(AddScheduleAsync);NewScheduleCommand=new AsyncCommand(()=>{SelectedSchedule=null;ScheduleName="New schedule";ScheduleTime="08:00";ScheduleDays="Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday";ScheduleEnabled=true;ScheduleWake=false;ScheduleStartMissed=true;ScheduleMaxRuntime=45;ScheduleReportMode="normal";Refill(ScheduleTopics,_db.Topics().Select(x=>new SourceSelection(x.Id,x.Name,true)));return Task.CompletedTask;});DeleteScheduleCommand=new AsyncCommand(DeleteScheduleAsync);
        EnableMonitoringCommand=new AsyncCommand(()=>SetMonitoringAsync(true));PauseMonitoringCommand=new AsyncCommand(()=>SetMonitoringAsync(false));
        RunNowCommand=new AsyncCommand(RunNowAsync);DiscoveryOnlyCommand=new AsyncCommand(()=>LaunchRunnerAsync(null,null,true));StopCommand=new AsyncCommand(StopAsync);
        LoadModelsCommand=new AsyncCommand(LoadModelsAsync);SelectModelCommand=new AsyncCommand(SelectModelAsync);
        TestModelCommand=new AsyncCommand(TestModelAsync);
        SaveAiSettingsCommand=new AsyncCommand(SaveAiSettingsAsync);
        CheckCodexCommand=new AsyncCommand(CheckCodexAsync);
        SignInCodexCommand=new AsyncCommand(SignInCodexAsync);
        CheckApiCommand=new AsyncCommand(CheckApiAsync);
        InstallFastCommand=new AsyncCommand(()=>InstallModelAsync("qwen/qwen3-4b-2507"));InstallQualityCommand=new AsyncCommand(()=>InstallModelAsync("qwen/qwen3.5-4b"));
        OpenReportCommand=new AsyncCommand(OpenReportAsync);OpenReportsFolderCommand=new AsyncCommand(()=>{Process.Start(new ProcessStartInfo(AppPaths.Reports){UseShellExecute=true});return Task.CompletedTask;});
        CopySummaryCommand=new AsyncCommand(CopySummaryAsync);ExportReportCommand=new AsyncCommand(ExportReportAsync);DeleteReportCommand=new AsyncCommand(DeleteReportAsync);ToggleBookmarkCommand=new AsyncCommand(ToggleBookmarkAsync);
        RunDiagnosticsCommand=new AsyncCommand(RunDiagnosticsAsync);CaptureCopiedCommand=new AsyncCommand(CaptureCopiedAsync);CaptureDokoBatchCommand=new AsyncCommand(CaptureDokoBatchAsync);
        ExampleTopicsCommand=new AsyncCommand(ExampleTopicsAsync);InstallBridgeCommand=new AsyncCommand(InstallBridgeAsync);
        InstallDokoCommand=new AsyncCommand(InstallDokoAsync);OpenLmStudioInstallCommand=new AsyncCommand(()=>{Process.Start(new ProcessStartInfo("https://lmstudio.ai/docs/developer/core/headless"){UseShellExecute=true});return Task.CompletedTask;});
        SaveSettingsCommand=new AsyncCommand(SaveSettingsAsync);
        SetupNextCommand=new AsyncCommand(()=>{SetupStep++;return Task.CompletedTask;});
        SetupBackCommand=new AsyncCommand(()=>{SetupStep--;return Task.CompletedTask;});
        AdvancedSetupCommand=new AsyncCommand(()=>{MainTabIndex=9;return Task.CompletedTask;});
        OpenTopicsCommand=new AsyncCommand(()=>{MainTabIndex=2;return Task.CompletedTask;});
        OpenScheduleCommand=new AsyncCommand(()=>{MainTabIndex=4;return Task.CompletedTask;});
        OpenReportsCommand=new AsyncCommand(()=>{MainTabIndex=7;return Task.CompletedTask;});
        OpenAiSettingsCommand=new AsyncCommand(()=>{MainTabIndex=5;return Task.CompletedTask;});
        InitializeReportControls();
        Refresh();
    }
    private void Refresh()
    {
        Refill(Topics,_db.Topics());Refill(Sources,_db.Sources());Refill(Schedules,_db.Schedules());Refill(Reports,_db.Reports().Select(x=>$"{x.Date} — {x.Path}"));Changed(nameof(LatestRun));Changed(nameof(SelectedModelKey));
    }
    private static void Refill<T>(ObservableCollection<T> target,IEnumerable<T> values){target.Clear();foreach(var x in values)target.Add(x);}
    private Task AddTopicAsync(){if(string.IsNullOrWhiteSpace(TopicName))throw new ArgumentException("Enter a topic name");long id=_db.AddTopic(TopicName.Trim(),priority:TopicPriority,maxAgeHours:TopicMaxAge,maxResults:TopicMaxResults);_db.ReplaceTerms(id,EditorTerms());SaveTopicRules(id);TopicName=TopicTerms=TopicExact=TopicExclude=TopicSynonyms=TopicAll=TopicDomains="";Refresh();Status="Topic added";return Task.CompletedTask;}
    private Task SaveTopicAsync(){if(SelectedTopic is null)throw new InvalidOperationException("Select a topic");_db.SetTopic(SelectedTopic.Id,TopicName.Trim(),TopicEnabled,TopicPriority,TopicMaxAge,TopicMaxResults);_db.ReplaceTerms(SelectedTopic.Id,EditorTerms());SaveTopicRules(SelectedTopic.Id);_db.SetTopicSources(SelectedTopic.Id,TopicSources.ToDictionary(x=>x.Id,x=>x.Enabled));Refresh();Status="Topic saved";return Task.CompletedTask;}
    private void SaveTopicRules(long id)=>_db.SetTopicRules(id,SplitCsv(TopicAll),SplitCsv(TopicDomains));
    private static string[] SplitCsv(string value)=>value.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    private IReadOnlyList<TopicTerm> EditorTerms()=>new[]{("include",TopicTerms),("exact",TopicExact),("exclude",TopicExclude),("synonym",TopicSynonyms)}.SelectMany(x=>x.Item2.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(term=>new TopicTerm(term,x.Item1))).ToArray();
    private Task ToggleSourceAsync(){if(SelectedSource is null)throw new InvalidOperationException("Select a source");_db.SetSourceEnabled(SelectedSource.Id,!SelectedSource.Enabled);Refresh();Status="Source updated";return Task.CompletedTask;}
    private Task AddSourceAsync(){if(string.IsNullOrWhiteSpace(SourceName)||!Uri.TryCreate(SourceUrl,UriKind.Absolute,out _))throw new ArgumentException("Enter a source name and URL");if(SourceType is not("rss" or "search" or "direct_web"))throw new ArgumentException("Choose RSS, search, or direct web");_db.AddSource(SourceName,SourceType,SourceUrl,"automated_direct");SourceName=SourceUrl="";Refresh();Status="Source added";return Task.CompletedTask;}
    private async Task AddScheduleAsync(){if(ScheduleMaxRuntime is <1 or >1440)throw new ArgumentException("Maximum run time must be 1 to 1440 minutes.");string[] days=ScheduleDays.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);var selected=ScheduleTopics.Where(x=>x.Enabled).Select(x=>x.Id).ToArray();long[] topics=selected.Length==ScheduleTopics.Count?[]:selected.Length==0?[-1]:selected;var schedule=new MonitorSchedule(SelectedSchedule?.Id??0,ScheduleName,ScheduleEnabled,ScheduleTime,days,ScheduleStartMissed,ScheduleWake,ScheduleMaxRuntime,topics,ScheduleReportMode);_db.SaveSchedule(schedule);Refresh();if(MonitoringEnabled)await UpdateSchedulerAsync();Status="Schedule saved";}
    private async Task DeleteScheduleAsync(){if(SelectedSchedule is null)return;_db.DeleteSchedule(SelectedSchedule.Id);Refresh();await UpdateSchedulerAsync();Status="Schedule removed";}
    private async Task SetMonitoringAsync(bool enabled){if(enabled&&_db.Schedules().Count==0)_db.SaveSchedule(new MonitorSchedule(0,"Daily 8 AM",true,"08:00",["Monday","Tuesday","Wednesday","Thursday","Friday","Saturday","Sunday"],true,false,45));_db.SetSetting("monitoring_enabled",enabled);MonitoringEnabled=enabled;await UpdateSchedulerAsync();Refresh();Status=enabled?"Monitoring enabled":"Monitoring paused";}
    private async Task UpdateSchedulerAsync(){string runner=Path.Combine(AppContext.BaseDirectory,"SignalAtlas.Runner.exe");if(!File.Exists(runner))throw new FileNotFoundException("Runner executable is missing. Publish the application first.",runner);await new TaskSchedulerService(_db,runner).RegisterAsync(CancellationToken.None);}
    private async Task RunNowAsync(){await LaunchRunnerAsync(null);}
    private async Task LaunchRunnerAsync(string? existingRun,string? captureFile=null,bool discoveryOnly=false)
    {
        SaveReportOptions();
        string runner=Path.Combine(AppContext.BaseDirectory,"SignalAtlas.Runner.exe");if(!File.Exists(runner))throw new FileNotFoundException("Runner executable is missing",runner);
        var psi=new ProcessStartInfo(runner){UseShellExecute=false,CreateNoWindow=true};if(existingRun is not null){psi.ArgumentList.Add("--existing-run");psi.ArgumentList.Add(existingRun);}
        if(captureFile is not null){psi.ArgumentList.Add("--capture-file");psi.ArgumentList.Add(captureFile);}
        if(discoveryOnly)psi.ArgumentList.Add("--discovery-only");
        using var process=Process.Start(psi)??throw new InvalidOperationException("Runner did not start");Status="Research running";await process.WaitForExitAsync();Refresh();Status=process.ExitCode==0?"Report ready":$"Run ended with code {process.ExitCode}";
    }
    private Task StopAsync(){var run=_db.LatestRun();if(run?.Status=="active"){File.WriteAllText(Path.Combine(AppPaths.State,"cancel-"+run.Id+".flag"),"cancel");Status="Stop requested";}return Task.CompletedTask;}
    private async Task LoadModelsAsync(){var models=await new LmStudioBackend(new WindowsResourceProbe()).InstalledAsync(CancellationToken.None);Refill(Models,models);Status=$"{models.Count} local models found";}
    private Task SelectModelAsync(){if(SelectedModel is null)throw new InvalidOperationException("Select a model");_db.SetSetting("selected_model",SelectedModel.Key);Changed(nameof(SelectedModelKey));Status="Model selected: "+SelectedModel.Name;return Task.CompletedTask;}
    private IModelBackend SelectedBackend()=>AiProvider switch
    {
        "openai_api"=>new OpenAiApiBackend(),
        "codex_chatgpt"=>new CodexCliBackend(),
        _=>new LmStudioBackend(new WindowsResourceProbe()){PreferredContext=PreferredContext,MinimumContext=MinimumContext,RestrictBattery=RestrictBattery}
    };
    private async Task TestModelAsync()
    {
        var backend=SelectedBackend();
        using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            Status="Testing selected AI provider";
            string model=AiProvider switch{"openai_api"=>OpenAiModel,"codex_chatgpt"=>"codex-chatgpt",_=>SelectedModelKey};
            if(!await backend.PrepareAsync(model,deadline.Token)){Status="AI test deferred: "+backend.DeferredReason;return;}
            var topic=new Topic(0,"procedural animation","",50,168,1,true,[new("procedural animation","include")]);
            var document=new Document(0,0,"https://example.org/test","Procedural animation test","This diagnostic text describes procedural animation techniques for research analysis. It contains no external claims.","",null,DateTimeOffset.UtcNow,true,0,1);
            var analysis=await backend.AnalyzeAsync(document,topic,deadline.Token);
            Status=analysis is null?"AI test returned no result":"AI test passed";
        }
        finally{await backend.CleanupAsync(CancellationToken.None);}
    }
    private Task SaveAiSettingsAsync()
    {
        string model=OpenAiModel.Trim();
        if(model.Length is <1 or >100 || !System.Text.RegularExpressions.Regex.IsMatch(model,"^[A-Za-z0-9._-]+$"))throw new ArgumentException("Enter a valid OpenAI model ID");
        _db.SetSetting("openai_model",model);_db.SetSetting("ai_provider",AiProvider);OpenAiModel=model;Status="AI provider settings saved";
        return Task.CompletedTask;
    }
    private async Task CheckApiAsync()
    {
        var backend=new OpenAiApiBackend();
        Status=await backend.PrepareAsync(OpenAiModel,CancellationToken.None)?"OpenAI API key is available. Run Test selected provider to check a request.":backend.DeferredReason??"OpenAI API is unavailable";
    }
    private async Task CheckCodexAsync()
    {
        var backend=new CodexCliBackend();
        Status=await backend.PrepareAsync("codex-chatgpt",CancellationToken.None)?"Codex CLI is signed in with ChatGPT. Run Test selected provider to check a request.":backend.DeferredReason??"Codex CLI is unavailable";
    }
    private Task SignInCodexAsync()
    {
        if(!CodexCliBackend.IsInstalled)throw new FileNotFoundException("Install Codex CLI first: npm install -g @openai/codex");
        Process.Start(new ProcessStartInfo("cmd.exe","/k codex login"){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Normal});
        Status="Complete ChatGPT sign-in in the browser, then click Check Codex sign-in";
        return Task.CompletedTask;
    }
    private async Task InstallModelAsync(string key){var lms=ProcessTool.FindExecutable("lms")??throw new FileNotFoundException("LM Studio CLI missing");Status="Downloading model: "+key;var result=await ProcessTool.RunAsync(lms,["get",key,"--yes"],TimeSpan.FromMinutes(45),CancellationToken.None);if(result.ExitCode!=0)throw new InvalidOperationException(result.Error);await LoadModelsAsync();}
    private string ChosenReportPath(){string? selected=SelectedReport;string? path=selected is not null && Reports.Contains(selected)?selected.Split(" — ",2).LastOrDefault():null;if(path is null||!File.Exists(path))throw new FileNotFoundException("Select an existing report");return path;}
    private Task OpenReportAsync(){Process.Start(new ProcessStartInfo(ChosenReportPath()){UseShellExecute=true});return Task.CompletedTask;}
    private Task CopySummaryAsync(){System.Windows.Clipboard.SetText(_db.ReportSummary(ChosenReportPath())??"No AI synthesis was available for this report.");Status="Summary copied";return Task.CompletedTask;}
    private Task ExportReportAsync(){string path=ChosenReportPath();var dialog=new Microsoft.Win32.SaveFileDialog{FileName=Path.GetFileName(path),DefaultExt=".html",Filter="HTML report (*.html)|*.html"};if(dialog.ShowDialog()==true){File.Copy(path,dialog.FileName,true);Status="Report exported";}return Task.CompletedTask;}
    private Task DeleteReportAsync(){string path=ChosenReportPath();_db.DeleteReport(path);Refresh();Status="Report deleted";return Task.CompletedTask;}
    private Task ToggleBookmarkAsync(){var entry=SelectedReportEntry??throw new InvalidOperationException("Select a report item");string path=ChosenReportPath();_db.SetBookmark(path,entry.DocumentId,entry.TopicId,!entry.Bookmarked);Refill(ReportEntries,_db.ReportEntries(path));SelectedReportEntry=ReportEntries.FirstOrDefault(x=>x.DocumentId==entry.DocumentId && x.TopicId==entry.TopicId);Status=entry.Bookmarked?"Bookmark removed":"Report item bookmarked";return Task.CompletedTask;}
    private async Task RunDiagnosticsAsync(){Refill(Diagnostics,await new DiagnosticService().RunAsync(true,CancellationToken.None));Status="Diagnostics finished";}
    private async Task CaptureCopiedAsync()
    {
        var topic=SelectedTopic??Topics.FirstOrDefault()??throw new InvalidOperationException("Choose a topic first");string url=FirstUrl();
        string text=string.IsNullOrWhiteSpace(CaptureText)?System.Windows.Clipboard.GetText():CaptureText;
        bool linkedIn=Uri.TryCreate(url,UriKind.Absolute,out var uri)&&SourcePolicy.IsLinkedIn(uri);
        string file=WriteCapture(new CaptureManifest(topic.Id,CaptureTitle,[url],text,false,linkedIn));Status="Starting capture and analysis";await LaunchRunnerAsync(null,file);
    }
    private async Task CaptureDokoBatchAsync()
    {
        if(BlockLinkedInReads)throw new InvalidOperationException("Turn off 'Block LinkedIn DokoBot reads' to enable this capture.");
        var topic=SelectedTopic??Topics.FirstOrDefault()??throw new InvalidOperationException("Choose a topic first");
        var urls=CaptureUrls.Split(['\r','\n',',',';'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct().ToArray();
        if(urls.Length==0)throw new ArgumentException("Enter one or more LinkedIn URLs");
        foreach(var url in urls)
        {
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||!SourcePolicy.IsLinkedIn(uri))throw new ArgumentException("Batch contains a non-LinkedIn URL: "+url);
        }
        string file=WriteCapture(new CaptureManifest(topic.Id,CaptureTitle,urls,null,true));
        Status=$"Starting {urls.Length} page DokoBot capture";await LaunchRunnerAsync(null,file);
    }
    private static string WriteCapture(CaptureManifest capture){string file=Path.Combine(AppPaths.State,"capture-"+Guid.NewGuid().ToString("N")+".json");File.WriteAllText(file,JsonSerializer.Serialize(capture));return file;}
    private string FirstUrl()=>CaptureUrls.Split(['\r','\n',',',';'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).FirstOrDefault()??throw new ArgumentException("Enter a LinkedIn URL");
    private Task ExampleTopicsAsync(){foreach(var name in new[]{"VFX","Motion capture","Game development","AI for game development","Procedural animation","Virtual production","Unreal Engine"})if(!_db.Topics().Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase))){long id=_db.AddTopic(name);_db.AddTerm(id,name,"include");}Refresh();Status="Example topics added";return Task.CompletedTask;}
    private async Task InstallBridgeAsync(){var doko=ProcessTool.FindDokoBot()??throw new FileNotFoundException("DokoBot CLI unavailable");var result=await ProcessTool.RunAsync(doko.File,doko.Prefix.Concat(["install-bridge","--browser","edge"]),TimeSpan.FromSeconds(60),CancellationToken.None);if(result.ExitCode!=0)throw new InvalidOperationException(result.Error);Status="Edge bridge installed; approve the DokoBot extension in your browser";}
    private async Task InstallDokoAsync()
    {
        string node=ProcessTool.FindExecutable("node")??throw new FileNotFoundException("Node.js is required. Install Node.js LTS before DokoBot.");
        string npm=Path.Combine(Path.GetDirectoryName(node)!,"node_modules","npm","bin","npm-cli.js");
        if(!File.Exists(npm))throw new FileNotFoundException("npm CLI was not found",npm);
        Status="Installing tested DokoBot CLI 2.11.0";
        var result=await ProcessTool.RunAsync(node,[npm,"install","-g","@dokobot/cli@2.11.0"],TimeSpan.FromMinutes(10),CancellationToken.None);
        if(result.ExitCode!=0)throw new InvalidOperationException(result.Error);Status="DokoBot installed. Install the browser bridge next.";
    }
    private Task SaveSettingsAsync()
    {
        if(MinimumContext<4096 || PreferredContext<MinimumContext || PreferredContext>32768)throw new ArgumentException("Context must be between 4096 and 32768, with preferred at least the minimum.");
        if(new[]{ReportsRetentionDays,ContentRetentionDays,CacheRetentionDays,LogsRetentionDays}.Any(x=>x<1||x>3650))throw new ArgumentException("Retention values must be 1 to 3650 days.");
        _db.SetSetting("preferred_context",PreferredContext);_db.SetSetting("minimum_context",MinimumContext);_db.SetSetting("restrict_battery",RestrictBattery);
        _db.SetSetting("reports_retention_days",ReportsRetentionDays);_db.SetSetting("content_retention_days",ContentRetentionDays);_db.SetSetting("cache_retention_days",CacheRetentionDays);_db.SetSetting("logs_retention_days",LogsRetentionDays);
        Status="Settings saved";return Task.CompletedTask;
    }
}
