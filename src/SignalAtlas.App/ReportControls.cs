using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Threading;
using SignalAtlas.Core;

namespace SignalAtlas.App;

public sealed class ReportModuleSelection(string key, string name, bool selected) : INotifyPropertyChanged
{
    private bool _selected = selected;
    public string Key { get; } = key;
    public string Name { get; } = name;
    public bool Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class MainViewModel
{
    private string _reportOutput = "both", _reportProfile = "research", _reportQuestion = "", _reportAudience = "";
    private int _reportPages = 5;
    private readonly DispatcherTimer _reportTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    public string ReportOutput { get => _reportOutput; set { _reportOutput = value; Changed(); } }
    public int ReportPages { get => _reportPages; set { _reportPages = value; Changed(); } }
    public string ReportProfile { get => _reportProfile; set { _reportProfile = value; Changed(); } }
    public string ReportQuestion { get => _reportQuestion; set { _reportQuestion = value; Changed(); } }
    public string ReportAudience { get => _reportAudience; set { _reportAudience = value; Changed(); } }
    public int[] ReportPageChoices { get; } = Enumerable.Range(1, 20).ToArray();
    public ObservableCollection<ReportModuleSelection> ReportModules { get; } = [];
    public ICommand SaveReportOptionsCommand { get; private set; } = null!;
    public ICommand OpenCardsCommand { get; private set; } = null!;
    public ICommand OpenNarrativeCommand { get; private set; } = null!;
    public ICommand OpenPdfCommand { get; private set; } = null!;
    public ICommand ExportCardsCommand { get; private set; } = null!;
    public ICommand ExportNarrativeCommand { get; private set; } = null!;
    public ICommand ExportPdfCommand { get; private set; } = null!;
    public string ReportProgress => _db.GetSetting("report_progress", "") ?? "";
    public bool HasCards => AvailableFile("cards") is not null;
    public bool HasResearchReport => AvailableFile("report") is not null;
    public bool HasPdf => AvailableFile("pdf") is not null;

    private void InitializeReportControls()
    {
        var options = _db.GetSetting("report_options", new ReportOptions()) ?? new ReportOptions();
        _reportOutput = options.Output; _reportPages = options.Pages; _reportProfile = options.Profile;
        _reportQuestion = options.Question; _reportAudience = options.Audience;
        foreach (var pair in SignalAtlas.Core.ReportModules.All) ReportModules.Add(new(pair.Key, pair.Value, options.Modules.Contains(pair.Key)));
        SaveReportOptionsCommand = new AsyncCommand(() => { SaveReportOptions(); Status = "Report preferences saved for manual and scheduled runs"; return Task.CompletedTask; });
        OpenCardsCommand = new AsyncCommand(() => OpenFormatAsync("cards"));
        OpenNarrativeCommand = new AsyncCommand(() => OpenFormatAsync("report"));
        OpenPdfCommand = new AsyncCommand(() => OpenFormatAsync("pdf"));
        ExportCardsCommand = new AsyncCommand(() => ExportFormatAsync("cards"));
        ExportNarrativeCommand = new AsyncCommand(() => ExportFormatAsync("report"));
        ExportPdfCommand = new AsyncCommand(() => ExportFormatAsync("pdf"));
        _reportTimer.Tick += (_, _) => { Changed(nameof(ReportProgress)); Changed(nameof(LatestRun)); };
        _reportTimer.Start();
    }
    private void SaveReportOptions()
    {
        var options = new ReportOptions { Output = ReportOutput, Pages = ReportPages, Profile = ReportProfile,
            Question = ReportQuestion.Trim(), Audience = ReportAudience.Trim(), Modules = ReportModules.Where(x => x.Selected).Select(x => x.Key).ToArray() };
        options.Validate(); _db.SetSetting("report_options", options);
    }
    private string? AvailableFile(string kind)
    {
        string? path = SelectedReport?.Split(" — ", 2).LastOrDefault();
        if (path is null) return null;
        var files = _db.ReportFiles(path);
        string? value = kind switch { "cards" => files.CardsHtml, "report" => files.ReportHtml, "pdf" => files.Pdf, _ => null };
        return value is not null && File.Exists(value) ? value : null;
    }
    private Task OpenFormatAsync(string kind)
    {
        string path = AvailableFile(kind) ?? throw new FileNotFoundException("This format is not available for the selected run.");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return Task.CompletedTask;
    }
    private Task ExportFormatAsync(string kind)
    {
        string path = AvailableFile(kind) ?? throw new FileNotFoundException("This format is not available for the selected run.");
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileName(path), DefaultExt = kind == "pdf" ? ".pdf" : ".html",
            Filter = kind == "pdf" ? "PDF report (*.pdf)|*.pdf" : "Standalone HTML (*.html)|*.html" };
        if (dialog.ShowDialog() == true)
        {
            if (kind == "pdf") File.Copy(path, dialog.FileName, true);
            else File.WriteAllText(dialog.FileName, Regex.Replace(File.ReadAllText(path), "<nav class=\"artifact-nav\".*?</nav>", "", RegexOptions.Singleline));
            Status = "Export saved";
        }
        return Task.CompletedTask;
    }
}
