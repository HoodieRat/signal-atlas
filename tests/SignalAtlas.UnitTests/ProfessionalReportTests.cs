using System.Text.Json;
using SignalAtlas.Core;
using SignalAtlas.Data;
using SignalAtlas.OpenAI;
using SignalAtlas.Reporting;

namespace SignalAtlas.UnitTests;

public sealed class ProfessionalReportTests
{
    [Theory]
    [InlineData(0)] [InlineData(21)]
    public void RejectsPageTargetsOutsideSupportedRange(int pages) => Assert.Throws<ArgumentException>(() => new ReportOptions { Pages = pages }.Validate());

    [Fact]
    public void LongerPageTargetsAllowMoreSubstantiveContent()
    {
        for (int pages = 2; pages <= 20; pages++)
            Assert.True(new ReportOptions { Pages = pages }.WordBudget > new ReportOptions { Pages = pages - 1 }.WordBudget);
    }

    [Fact]
    public void OnePagePdfHasRealPaginationAndCitedHtmlEscapesInput()
    {
        var report = Example(1);
        var pdf = new PdfReportRenderer().Render(report);
        Assert.Equal(1, pdf.Pages);
        Assert.True(pdf.Bytes.Length > 1000);
        var html = new HtmlReportRenderer().RenderReport(report with { Title = "<script>bad</script>" });
        Assert.Contains("&lt;script&gt;bad&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("href=\"#source-1\"", html);
        Assert.Contains("id=\"source-1\"", html);
    }

    [Fact]
    public void PublicationRejectsPdfOverTheSelectedLimit()
    {
        var run = new RunSummary("test", "COMPLETED", "PUBLISHING", 0, 0, 0, 0, 0, null, DateTimeOffset.UtcNow, null);
        Assert.Throws<InvalidDataException>(() => HtmlReportRenderer.PublishBundle(run, [], null, Example(1), new([], 2), new ReportOptions { Pages = 1 }));
    }

    [Fact]
    public void ReportPreferencesAndArtifactPathsSurviveDatabaseRoundTrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ReportDataTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var db = new ResearchDatabase(Path.Combine(dir, "research.db"), Path.Combine(dir, "documents")); db.Initialize();
            var options = new ReportOptions { Output = "both", Pages = 20, Profile = "technical", Modules = ["metrics", "appendix"] };
            db.SetSetting("report_options", options);
            Assert.Equal(20, db.GetSetting("report_options", new ReportOptions())!.Pages);
            long topic = db.AddTopic("Animation");
            string run = db.ImportChosenContent(topic, "https://example.com", "Source", "Retained source text for detailed research analysis and verification.", requireLinkedIn:false);
            var files = new ReportArtifacts("primary.html", "cards.html", "report.html", "report.pdf", "report.json");
            db.SaveReport(run, files.PrimaryHtml, "Summary", false, files);
            Assert.Equal(files, db.ReportFiles(files.PrimaryHtml));
            Assert.Contains("Retained source text", Assert.Single(db.ReportItems(run, true)).Document.Text);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ComposerPlansWritesAndConcludesUsingRetainedEvidence()
    {
        var model = new ScriptedModel();
        var source = new Source(1, "Publisher", "rss", null, "automated_direct", true, null);
        var topic = new Topic(1, "Animation", "", 50, 100, 10, true, []);
        var doc = new Document(1, 1, "https://example.com", "Study", "Retained detailed source evidence.", "hash", null, DateTimeOffset.UtcNow, true, 1, 1);
        var item = new ReportItem(doc, topic, source, new(1, "test", 90, 50, "Summary.", "Match", "{}"));
        var report = await new ReportComposer(model).ComposeAsync([item], new ReportOptions { Pages = 3, Modules = [] }, null, CancellationToken.None);
        Assert.Equal(3, model.Inputs.Count);
        Assert.Contains("Retained detailed source evidence", model.Inputs[1]);
        Assert.Single(report.Chapters); Assert.Single(report.Sources);
        Assert.Equal([1], report.Chapters[0].Paragraphs[0].SourceIds);
    }

    [Theory]
    [InlineData("citation")]
    [InlineData("chart")]
    [InlineData("missing-table")]
    public async Task ComposerRejectsUnsupportedEvidenceAndMissingSpecializedElements(string failure)
    {
        var model = new ScriptedModel(failure);
        var source = new Source(1, "Publisher", "rss", null, "automated_direct", true, null);
        var topic = new Topic(1, "Animation", "", 50, 100, 10, true, []);
        var doc = new Document(1, 1, "https://example.com", "Study", "The baseline took 12 milliseconds.", "hash", null, DateTimeOffset.UtcNow, true, 1, 1);
        var item = new ReportItem(doc, topic, source, new(1, "test", 90, 50, "Summary.", "Match", "{}"));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new ReportComposer(model).ComposeAsync([item],
            new ReportOptions { Pages = 1, Modules = failure == "missing-table" ? ["comparisons"] : [] }, null, CancellationToken.None));
        Assert.Contains("validation", error.Message);
        Assert.Equal(3, model.Inputs.Count); // The invalid chapter is retried once, then rejected.
    }

    public static ResearchReport Example(int pages) => new("Evaluating animation workflows", "A decision-focused research assessment",
        [new("The available evidence supports a bounded pilot before adoption. The source reports a measurable improvement under controlled conditions, but does not establish performance in production.", [1])],
        [new("Evidence and practical implications", "analysis", [new("The comparison isolates a specific workflow. A team should reproduce those conditions and measure quality as well as speed before changing its production process.", [1])], [], [])],
        [new("Proceed with a small, instrumented trial and document conditions that could change the result.", [1])],
        [new(1, "Workflow evaluation", "Example research team", "https://example.com/study", "Animation", null, DateTimeOffset.UtcNow, "Summary", "The baseline took 12 milliseconds and the pilot took 8 milliseconds.")],
        new ReportOptions { Pages = pages }, DateTimeOffset.UtcNow, ["This is a layout test fixture, not a published research finding."]);

    private sealed class ScriptedModel(string? failure = null) : IModelBackend
    {
        public List<string> Inputs { get; } = [];
        public string? DeferredReason => null;
        public Task<bool> PrepareAsync(string modelKey, CancellationToken token) => Task.FromResult(true);
        public Task<Analysis?> AnalyzeAsync(Document d, Topic t, CancellationToken token) => Task.FromResult<Analysis?>(null);
        public Task<ResearchBrief?> SynthesizeAsync(IReadOnlyList<ReportItem> i, CancellationToken token) => Task.FromResult<ResearchBrief?>(null);
        public Task CleanupAsync(CancellationToken token) => Task.CompletedTask;
        public Task<string> WriteReportPartAsync(string input, object schema, int maxTokens, CancellationToken token)
        {
            Inputs.Add(input);
            if (failure is not null && Inputs.Count > 1)
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    paragraphs = new[] { new { text = "A claimed result.", source_ids = new[] { failure == "citation" ? 99 : 1 } } },
                    tables = Array.Empty<object>(),
                    charts = failure == "chart" ? new object[] { new { caption = "Invented measurements", unit = "milliseconds", values = new[]
                    {
                        new { label = "Baseline", value = 12, source_id = 1, evidence_quote = "The baseline took 12 milliseconds." },
                        new { label = "Pilot", value = 8, source_id = 1, evidence_quote = "The pilot took 8 milliseconds." }
                    } } } : Array.Empty<object>()
                }));
            object result = Inputs.Count switch
            {
                1 => new { title = "Animation evidence", subtitle = "Scope and findings", chapters = new[] { new { heading = "Evidence", purpose = "Assess evidence", kind = failure == "missing-table" ? "comparisons" : "analysis", source_ids = new[] { 1 } } } },
                2 => new { paragraphs = new[] { new { text = "A supported analysis.", source_ids = new[] { 1 } } }, tables = Array.Empty<object>(), charts = Array.Empty<object>() },
                _ => (object)new { executive_summary = new[] { new { text = "The principal finding.", source_ids = new[] { 1 } } }, conclusion = new[] { new { text = "A qualified conclusion.", source_ids = new[] { 1 } } } }
            };
            return Task.FromResult(JsonSerializer.Serialize(result));
        }
    }
}
