using System.Text.Json.Serialization;

namespace SignalAtlas.Core;

public sealed record ReportOptions
{
    public string Output { get; init; } = "both";
    public int Pages { get; init; } = 5;
    public string Profile { get; init; } = "research";
    public string Question { get; init; } = "";
    public string Audience { get; init; } = "An informed reader who needs to understand the evidence and make decisions";
    public string[] Modules { get; init; } = ["comparisons", "risks", "recommendations", "methodology"];
    public bool WantsReport => Output is "report" or "both";
    public bool WantsCards => Output is "cards" or "both";
    public int WordBudget => Pages == 1 ? 300 : Pages * 330 - (Pages >= 8 ? 400 : 100);
    public void Validate()
    {
        if (Output is not ("cards" or "report" or "both")) throw new ArgumentException("Choose cards, report, or both.");
        if (Pages is < 1 or > 20) throw new ArgumentException("Report length must be between 1 and 20 pages.");
        if (Profile is not ("research" or "technical" or "business")) throw new ArgumentException("Choose a report profile.");
        if (Question.Length > 2000 || Audience.Length > 500) throw new ArgumentException("Shorten the report question or audience description.");
        if (Modules is null || Modules.Any(x => !ReportModules.All.ContainsKey(x))) throw new ArgumentException("Unknown report section type.");
    }
}

public static class ReportModules
{
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        ["comparisons"] = "Comparison tables",
        ["timeline"] = "Timeline / chronology",
        ["risks"] = "Risk register and mitigations",
        ["recommendations"] = "Recommendations and action plan",
        ["glossary"] = "Glossary of specialist terms",
        ["metrics"] = "Quantitative tables and charts",
        ["methodology"] = "Methods and evidence quality",
        ["appendix"] = "Technical / research appendix"
    };
}

public sealed record EvidenceSource(int Id, string Title, string Publisher, string? Url, string Topic,
    DateTimeOffset? PublishedUtc, DateTimeOffset RetrievedUtc, string Summary, [property: JsonIgnore] string Text);
public sealed record CitedParagraph(string Text, int[] SourceIds);
public sealed record ReportTableRow(string[] Cells, int[] SourceIds);
public sealed record ReportTable(string Caption, string[] Columns, ReportTableRow[] Rows);
public sealed record ReportMetric(string Label, double Value, int SourceId, string EvidenceQuote);
public sealed record ReportChart(string Caption, string Unit, ReportMetric[] Values);
public sealed record ReportChapter(string Heading, string Kind, CitedParagraph[] Paragraphs, ReportTable[] Tables, ReportChart[] Charts);
public sealed record ResearchReport(string Title, string Subtitle, CitedParagraph[] ExecutiveSummary,
    ReportChapter[] Chapters, CitedParagraph[] Conclusion, EvidenceSource[] Sources, ReportOptions Options,
    DateTimeOffset CreatedUtc, string[] CoverageNotes);
public sealed record ReportChapterPlan(string Heading, string Purpose, string Kind, int[] SourceIds);
public sealed record ReportPlan(string Title, string Subtitle, ReportChapterPlan[] Chapters);
public sealed record ReportArtifacts(string PrimaryHtml, string? CardsHtml, string? ReportHtml, string? Pdf, string? DataJson)
{
    public IEnumerable<string> Paths => new[] { PrimaryHtml, CardsHtml, ReportHtml, Pdf, DataJson }.OfType<string>().Distinct();
}
