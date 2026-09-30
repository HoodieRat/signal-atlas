using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SignalAtlas.Core;

public sealed record Topic(long Id, string Name, string Description, int Priority, int MaxAgeHours, int MaxResultsPerRun, bool Enabled, IReadOnlyList<TopicTerm> Terms,IReadOnlyList<string>? AllKeywords=null,IReadOnlyList<string>? Domains=null);
public sealed record TopicTerm(string Term, string Type, double Weight = 1);
public sealed record Source(long Id, string Name, string Type, string? BaseUri, string PolicyMode, bool Enabled, string? ConfigJson)
{
    public string DisplayPolicy => PolicyMode=="discovery_only" && BaseUri is not null && Uri.TryCreate(BaseUri,UriKind.Absolute,out var uri) && SourcePolicy.IsLinkedIn(uri)
        ? "LinkedIn setting controlled" : PolicyMode.Replace('_',' ');
}
public sealed record Discovery(long SourceId, long TopicId, string Url, string CanonicalUrl, string Title, string Snippet, DateTimeOffset? PublishedUtc, DateTimeOffset DiscoveredUtc, bool FetchAllowed, double Score);
public sealed record Document(long Id, long SourceId, string? Url, string Title, string Text, string ContentHash, DateTimeOffset? PublishedUtc, DateTimeOffset FirstSeenUtc, bool IsNew, long TopicId, double DeterministicScore);
public sealed record Analysis(long DocumentId, string ModelKey, double RelevanceScore, double NoveltyScore, string Summary, string RelevanceReason, string RawJson);
public sealed record ReportItem(Document Document, Topic Topic, Source Source, Analysis? Analysis,DateTimeOffset? DiscoveredUtc=null);
public sealed record ReportFinding(string Heading, string Analysis, IReadOnlyList<int> SourceIds);
public sealed record ResearchBrief(string ExecutiveSummary, IReadOnlyList<ReportFinding> Findings, string Implications, string Limitations);
public static class ReportEvidence
{
    public static IReadOnlyList<ReportItem> Select(IReadOnlyList<ReportItem> items) => items
        .Where(x => x.Document.IsNew && x.Analysis is not null)
        .DistinctBy(x => x.Document.Id)
        .OrderByDescending(x => x.Analysis!.RelevanceScore)
        .Take(30)
        .ToArray();
}
public sealed record RunSummary(string Id, string Status, string Phase, int Discovered, int Fetched, int Analyzed, int Duplicates, int Failed, string? DeferredReason, DateTimeOffset StartedUtc, DateTimeOffset? FinishedUtc);
public sealed record MonitorSchedule(long Id,string Name,bool Enabled,string TimeOfDay,string[] Days,bool StartWhenAvailable,bool WakeToRun,int MaxRuntimeMinutes,long[]? TopicIds=null,string ReportMode="normal");
public sealed record CaptureManifest(long TopicId,string Title,string[] Urls,string? Text,bool UseDokoBot,bool RequireLinkedIn=true);

public static class AppPaths
{
    public static string Root => Environment.GetEnvironmentVariable("SIGNALATLAS_DATA_ROOT") is {Length:>0} value?Path.GetFullPath(value):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SignalAtlas");
    public static string Database => Path.Combine(Root, "data", "research.db");
    public static string Documents => Path.Combine(Root, "documents");
    public static string State => Path.Combine(Root, "state");
    public static string Logs => Path.Combine(Root, "logs");
    public static string Reports => Environment.GetEnvironmentVariable("SIGNALATLAS_REPORTS_ROOT") is {Length:>0} value?Path.GetFullPath(value):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Signal Atlas");
    public static void Ensure()
    {
        foreach (var path in new[] { Path.GetDirectoryName(Database)!, Documents, State, Logs, Reports, Path.Combine(Reports, "Reports"), Path.Combine(Root, "cache", "temporary") }) Directory.CreateDirectory(path);
    }
}

public static class RunPhases
{
    public static readonly string[] Ordered = ["QUEUED", "PRECHECK", "COLLECTING", "NORMALIZING", "DEDUPLICATING", "MODEL_PREP", "ANALYZING", "SYNTHESIZING", "PUBLISHING", "CLEANUP"];
    public static readonly HashSet<string> Terminal = ["COMPLETED", "PARTIAL", "DEFERRED", "FAILED", "CANCELLED"];
    public static bool CanTransition(string from, string to)
    {
        if (Terminal.Contains(from)) return false;
        if (Terminal.Contains(to)) return true;
        int a = Array.IndexOf(Ordered, from), b = Array.IndexOf(Ordered, to);
        return a >= 0 && b == a + 1;
    }
}

public static class SourcePolicy
{
    public static bool IsLinkedIn(Uri uri) => uri.Host.Equals("linkedin.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".linkedin.com", StringComparison.OrdinalIgnoreCase);
    public static bool MayFetch(Uri uri, Source source, bool blockLinkedInReads = true)
    {
        if (uri.Scheme is not ("http" or "https")) return false;
        if (IsLinkedIn(uri)) return !blockLinkedInReads && source.Type == "search" && source.PolicyMode == "discovery_only";
        return source.PolicyMode == "automated_direct";
    }
    public static bool MayManualImport(Source source, bool userInitiated) => userInitiated && source.PolicyMode == "manual_import_only";
    public static bool MayUserInitiatedBrowserCapture(Uri uri, bool blockLinkedInCapture, bool userInitiated) => userInitiated && uri.Scheme is "http" or "https" && (!IsLinkedIn(uri) || !blockLinkedInCapture);
    public static bool IsValid(Source source)
    {
        if (source.PolicyMode is not ("automated_direct" or "discovery_only" or "official_api_only" or "manual_import_only")) return false;
        if(source.BaseUri is null)return true;
        if(!Uri.TryCreate(source.BaseUri,UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https"))return false;
        return !IsLinkedIn(uri) || source.PolicyMode!="automated_direct";
    }
}

public static class ContentTools
{
    private static readonly HashSet<string> Tracking = ["fbclid", "gclid", "msclkid", "ref_src"];
    public static string Canonicalize(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("A valid HTTP URL is required.", nameof(url));
        var builder = new UriBuilder(uri) { Fragment = "", Host = uri.IdnHost.ToLowerInvariant(), Scheme = "https", Port = -1 };
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2)).Where(x => !x[0].StartsWith("utm_", StringComparison.OrdinalIgnoreCase) && !Tracking.Contains(x[0].ToLowerInvariant()))
            .OrderBy(x => x[0], StringComparer.Ordinal).ThenBy(x => x.Length > 1 ? x[1] : "", StringComparer.Ordinal)
            .Select(x => string.Join("=", x));
        builder.Query = string.Join("&", query);
        builder.Path = uri.AbsolutePath.TrimEnd('/') is { Length: > 0 } path ? path : "/";
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }
    public static string Normalize(string text) => Regex.Replace(System.Net.WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(text).ToLowerInvariant()))).ToLowerInvariant();
    public static double Similarity(string a, string b)
    {
        var x = Shingles(a); var y = Shingles(b);
        if (x.Count == 0 || y.Count == 0) return 0;
        return 2.0 * x.Intersect(y).Count() / (x.Count + y.Count);
    }
    private static HashSet<string> Shingles(string s)
    {
        var words=Regex.Matches(s.ToLowerInvariant(),@"[\p{L}\p{N}]{3,}").Select(m=>m.Value).ToArray();
        if(words.Length<5)return words.ToHashSet();
        var set=new HashSet<string>();for(int i=0;i<=words.Length-5;i++)set.Add(string.Join(' ',words.Skip(i).Take(5)));return set;
    }
    public static double Score(Topic topic, string title, string snippet,string? url=null)
    {
        string haystack = $"{title} {snippet}";
        if(topic.AllKeywords is {Count:>0} && topic.AllKeywords.Any(t=>!haystack.Contains(t,StringComparison.OrdinalIgnoreCase)))return 0;
        if(topic.Domains is {Count:>0} && url is not null)
        {
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || !topic.Domains.Any(d=>uri.Host.Equals(d,StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith("."+d,StringComparison.OrdinalIgnoreCase)))return 0;
        }
        if (topic.Terms.Any(t => t.Type == "exclude" && haystack.Contains(t.Term, StringComparison.OrdinalIgnoreCase))) return 0;
        var positive = topic.Terms.Where(t => t.Type is "include" or "exact" or "synonym").ToArray();
        if (positive.Length == 0) return haystack.Contains(topic.Name, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        double total = positive.Sum(t => t.Weight * (t.Type == "exact" ? 1.5 : 1));
        double hit = positive.Where(t => haystack.Contains(t.Term, StringComparison.OrdinalIgnoreCase)).Sum(t => t.Weight * (t.Type == "exact" ? 1.5 : 1));
        return total <= 0 ? 0 : hit / total;
    }
    public static string Query(Topic topic)
    {
        var positive = topic.Terms.Where(t => t.Type is "exact" or "include").Select(t => t.Type == "exact" ? $"\"{t.Term}\"" : t.Term).Take(5).ToArray();
        string baseQuery = positive.Length > 0 ? string.Join(" ", positive) : $"\"{topic.Name}\"";
        if(topic.AllKeywords is {Count:>0})baseQuery+=" "+string.Join(" ",topic.AllKeywords.Select(x=>$"\"{x}\""));
        return baseQuery + string.Concat(topic.Terms.Where(t => t.Type == "exclude").Take(5).Select(t => $" -\"{t.Term}\""));
    }
}

public interface ICollector { string Type { get; } Task<IReadOnlyList<Discovery>> DiscoverAsync(Topic topic, Source source, CancellationToken cancellationToken); Task<string?> FetchAsync(Discovery discovery, CancellationToken cancellationToken); }
public interface ISourcePolicy { bool MayFetch(Uri uri, Source source); }
public interface IModelBackend { string? DeferredReason { get; } int ReportSourceCharacterLimit => 16000; Task<bool> PrepareAsync(string modelKey, CancellationToken cancellationToken); Task<Analysis?> AnalyzeAsync(Document document, Topic topic, CancellationToken cancellationToken); Task<ResearchBrief?> SynthesizeAsync(IReadOnlyList<ReportItem> items, CancellationToken cancellationToken); Task<string> WriteReportPartAsync(string input, object schema, int maxTokens, CancellationToken cancellationToken); Task CleanupAsync(CancellationToken cancellationToken); }
public interface IResourceProbe { ResourceSnapshot Sample(); }
public sealed record ResourceSnapshot(long AvailableRamBytes, long FreeDiskBytes, long? GpuBudgetBytes, long? GpuUsageBytes, double? CpuPercent, int? BatteryPercent, bool? OnAcPower);
public interface IReportRenderer { string Render(RunSummary run, IReadOnlyList<ReportItem> items, ResearchBrief? synthesis, bool discoveryOnly = false); }
public interface IScheduler { Task RegisterAsync(CancellationToken cancellationToken); Task RemoveAsync(CancellationToken cancellationToken); }
public interface IContentNormalizer { string Normalize(string text); }
public interface IDeduplicator { bool IsNearDuplicate(string text, IEnumerable<string> recent); }
