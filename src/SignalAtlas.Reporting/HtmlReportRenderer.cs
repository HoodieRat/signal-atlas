using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using SignalAtlas.Core;

namespace SignalAtlas.Reporting;

public sealed class HtmlReportRenderer : IReportRenderer
{
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    public string Render(RunSummary run, IReadOnlyList<ReportItem> items, ResearchBrief? synthesis, bool discoveryOnly = false) =>
        new CardDigestRenderer().Render(run, items, discoveryOnly ? "Discovery-only run: source links and context are available below." : synthesis?.ExecutiveSummary);

    public string RenderReport(ResearchReport report)
    {
        var b = new StringBuilder();
        b.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>").Append(E(report.Title)).Append("</title><style>");
        b.Append("*{box-sizing:border-box}body{margin:0;background:#e9eeec;color:#203139;font:17px/1.75 Georgia,serif}a{color:#1b6754;text-underline-offset:3px}.document{max-width:1040px;margin:28px auto;background:white;padding:64px 84px;box-shadow:0 4px 30px #173c3410}header{padding-bottom:32px;border-bottom:3px solid #1b5d4f}.eyebrow,.meta,nav,.caption,footer{font-family:Arial,sans-serif}.eyebrow{text-transform:uppercase;letter-spacing:.16em;font-size:11px;font-weight:bold;color:#1b5d4f}h1{font:600 40px/1.14 Georgia,serif;max-width:820px;margin:20px 0}h2{font:600 25px/1.3 Georgia,serif;color:#1b5d4f;margin:42px 0 16px;break-after:avoid}h3{font-size:20px}.subtitle{font-size:21px;color:#566863}.meta{font-size:12px;color:#60746b}.question{border-left:3px solid #c4a45c;padding-left:18px;margin:24px 0}.toc{background:#f3f6f4;padding:22px 30px;margin:32px 0;font-size:14px}.toc a{display:block;margin:6px 0}.summary{font-size:19px}.cite{font:11px Arial,sans-serif;vertical-align:super;white-space:nowrap}.caption{font-size:12px;font-weight:700;color:#38594d;margin:24px 0 8px}table{width:100%;border-collapse:collapse;font:13px/1.5 Arial,sans-serif;margin:10px 0 26px}th{background:#e2ece6;text-align:left}th,td{padding:10px;border-bottom:1px solid #cedbd3;vertical-align:top;overflow-wrap:anywhere}tr:nth-child(even){background:#f7f9f8}.references{font-size:13px;overflow-wrap:anywhere}.references li{margin:12px 0}.notes{font:12px/1.6 Arial,sans-serif;color:#617269;border-top:1px solid #ccd9d1;margin-top:20px;padding-top:15px}figure{margin:22px 0;break-inside:avoid}svg{max-width:100%;height:auto}footer{border-top:2px solid #1b5d4f;padding-top:16px;margin-top:35px;font-size:11px;color:#617269}@media(max-width:700px){.document{padding:28px 22px;margin:0}h1{font-size:30px}table{font-size:11px}th,td{padding:6px}}@media print{@page{size:letter;margin:18mm}body{background:white;font-size:11pt}.document{margin:0;padding:0;max-width:none;box-shadow:none}.artifact-nav{display:none}h1{font-size:27pt}h2{font-size:17pt}table{font-size:9pt}thead{display:table-header-group}tr{break-inside:avoid}.toc{background:white}.cite{font-size:8pt}}");
        b.Append("</style></head><body><main class=\"document\"><header><div class=\"eyebrow\">").Append(E(report.Options.Profile)).Append(" / Research report</div><h1>").Append(E(report.Title)).Append("</h1><p class=\"subtitle\">").Append(E(report.Subtitle)).Append("</p><p class=\"meta\">").Append(E(report.CreatedUtc.ToLocalTime().ToString("dd MMMM yyyy"))).Append(" · ").Append(report.Sources.Length).Append(" cited sources · Prepared for ").Append(E(report.Options.Audience)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(report.Options.Question)) b.Append("<p class=\"question\"><strong>Research question</strong><br>").Append(E(report.Options.Question)).Append("</p>");
        b.Append("</header>");
        if (report.Options.Pages > 2)
        {
            b.Append("<nav class=\"toc\" aria-label=\"Contents\"><strong>Contents</strong><a href=\"#executive\">Executive summary</a>");
            for (int i = 0; i < report.Chapters.Length; i++) b.Append("<a href=\"#chapter-").Append(i).Append("\">").Append(i + 1).Append(". ").Append(E(report.Chapters[i].Heading)).Append("</a>");
            b.Append("<a href=\"#conclusions\">Conclusions</a><a href=\"#references\">References and research scope</a></nav>");
        }
        b.Append("<section id=\"executive\" class=\"summary\"><h2>Executive summary</h2>");
        foreach (var p in report.ExecutiveSummary) Paragraph(p);
        b.Append("</section>");
        int table = 0, figure = 0;
        for (int i = 0; i < report.Chapters.Length; i++)
        {
            var c = report.Chapters[i]; b.Append("<section id=\"chapter-").Append(i).Append("\"><h2>").Append(i + 1).Append(". ").Append(E(c.Heading)).Append("</h2>");
            foreach (var p in c.Paragraphs) Paragraph(p);
            foreach (var t in c.Tables)
            {
                b.Append("<p class=\"caption\">Table ").Append(++table).Append(". ").Append(E(t.Caption)).Append("</p><table><thead><tr>");
                foreach (string col in t.Columns) b.Append("<th scope=\"col\">").Append(E(col)).Append("</th>");
                b.Append("</tr></thead><tbody>");
                foreach (var row in t.Rows) { b.Append("<tr>"); for (int cell = 0; cell < row.Cells.Length; cell++) { b.Append("<td>").Append(E(row.Cells[cell])); if (cell == row.Cells.Length - 1) Cite(row.SourceIds); b.Append("</td>"); } b.Append("</tr>"); }
                b.Append("</tbody></table>");
            }
            foreach (var chart in c.Charts)
            {
                b.Append("<figure><figcaption class=\"caption\">Figure ").Append(++figure).Append(". ").Append(E(chart.Caption)).Append(" (").Append(E(chart.Unit)).Append(")</figcaption>");
                int height = chart.Values.Length * 42 + 25;
                b.Append("<svg role=\"img\" aria-label=\"").Append(E(chart.Caption)).Append("\" viewBox=\"0 0 760 ").Append(height).Append("\">");
                double max = Math.Max(1, chart.Values.Max(x => x.Value));
                for (int row = 0; row < chart.Values.Length; row++)
                {
                    var v = chart.Values[row]; int y = row * 42 + 12;
                    b.Append("<text x=\"0\" y=\"").Append(y + 18).Append("\" font-family=\"Arial\" font-size=\"12\">").Append(E(v.Label.Length > 28 ? v.Label[..28] + "…" : v.Label)).Append("</text>");
                    b.Append("<rect x=\"200\" y=\"").Append(y).Append("\" width=\"").Append((v.Value / max * 420).ToString("0.##", CultureInfo.InvariantCulture)).Append("\" height=\"26\" fill=\"#1b5d4f\"/>");
                    b.Append("<text x=\"630\" y=\"").Append(y + 18).Append("\" font-family=\"Arial\" font-size=\"12\">").Append(v.Value.ToString("0.##", CultureInfo.InvariantCulture)).Append(" [").Append(v.SourceId).Append("]</text>");
                }
                b.Append("</svg><p class=\"meta\">Source measurements: "); Cite(chart.Values.Select(x => x.SourceId)); b.Append("</p></figure>");
            }
            b.Append("</section>");
        }
        b.Append("<section id=\"conclusions\"><h2>Conclusions</h2>"); foreach (var p in report.Conclusion) Paragraph(p); b.Append("</section>");
        b.Append("<section id=\"references\"><h2>References and research scope</h2><ol class=\"references\">");
        foreach (var s in report.Sources)
        {
            b.Append("<li id=\"source-").Append(s.Id).Append("\" value=\"").Append(s.Id).Append("\">");
            if (Uri.TryCreate(s.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") b.Append("<a href=\"").Append(E(uri.AbsoluteUri)).Append("\" rel=\"noopener noreferrer\">").Append(E(s.Title)).Append("</a>"); else b.Append(E(s.Title));
            b.Append(". ").Append(E(s.Publisher)).Append(". Published ").Append(E(s.PublishedUtc?.ToString("dd MMM yyyy") ?? "date not stated")).Append("; accessed ").Append(E(s.RetrievedUtc.ToString("dd MMM yyyy"))).Append(".</li>");
        }
        b.Append("</ol><div class=\"notes\">"); foreach (string note in report.CoverageNotes) b.Append("<p>").Append(E(note)).Append("</p>");
        b.Append("</div></section><footer>Prepared by Signal Atlas · Evidence and interpretation are cited throughout. The PDF edition supplies final pagination.</footer></main></body></html>");
        return b.ToString();
        void Paragraph(CitedParagraph p) { b.Append("<p>").Append(E(p.Text)); Cite(p.SourceIds); b.Append("</p>"); }
        void Cite(IEnumerable<int> ids) { b.Append(' '); foreach (int id in ids.Distinct()) b.Append("<a class=\"cite\" href=\"#source-").Append(id).Append("\">[").Append(id).Append("]</a>"); }
    }

    public static ReportArtifacts PublishBundle(RunSummary run, IReadOnlyList<ReportItem> items, ResearchBrief? brief,
        ResearchReport? report, RenderedPdf? pdf, ReportOptions options, string? notice = null)
    {
        options.Validate();
        if(pdf is not null && pdf.Pages>options.Pages)throw new InvalidDataException("The PDF exceeds the selected page limit.");
        AppPaths.Ensure();
        string stem = Path.Combine(AppPaths.Reports, "Reports", DateTimeOffset.Now.ToString("yyyy-MM-dd_HHmmss") + "_" + run.Id[..Math.Min(8, run.Id.Length)]);
        string? cardsPath = options.WantsCards || report is null ? stem + ".cards.html" : null;
        string? reportPath = report is not null ? stem + ".report.html" : null;
        string? pdfPath = pdf is not null ? stem + ".pdf" : null;
        string? dataPath = report is not null ? stem + ".report.json" : null;
        string nav = "<nav class=\"artifact-nav\" style=\"font:14px Arial,sans-serif;background:#173c34;color:white;padding:16px;display:flex;gap:24px;justify-content:center\">";
        foreach (var (label, path) in new[] { ("Card digest", cardsPath), ("Research report", reportPath), ("PDF · " + pdf?.Pages + " pages", pdfPath) })
            if (path is not null) nav += "<a style=\"color:white\" href=\"" + E(Path.GetFileName(path)) + "\">" + E(label) + "</a>";
        nav += "</nav>";
        var renderer = new HtmlReportRenderer();
        if (cardsPath is not null)
        {
            string html = renderer.Render(run, items, brief);
            if (notice is not null) html = html.Replace("<main>", "<main><p role=\"status\" style=\"padding:1rem;border-left:4px solid #ba9135;background:#fff7e6\">" + E(notice) + "</p>");
            Write(cardsPath, html.Replace("<body>", "<body>" + nav));
        }
        if (reportPath is not null) Write(reportPath, renderer.RenderReport(report!).Replace("<body>", "<body>" + nav));
        if (pdfPath is not null) { File.WriteAllBytes(pdfPath + ".tmp", pdf!.Bytes); File.Move(pdfPath + ".tmp", pdfPath, true); }
        if (dataPath is not null) Write(dataPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        string primary = cardsPath ?? reportPath!;
        string latest = File.ReadAllText(primary);
        foreach (var path in new[] { cardsPath, reportPath, pdfPath }.Where(x => x is not null))
            latest = latest.Replace("href=\"" + Path.GetFileName(path) + "\"", "href=\"Reports/" + Path.GetFileName(path) + "\"");
        Write(Path.Combine(AppPaths.Reports, "Latest Report.html"), latest);
        return new(primary, cardsPath, reportPath, pdfPath, dataPath);
    }
    private static void Write(string path, string content) { File.WriteAllText(path + ".tmp", content, Encoding.UTF8); File.Move(path + ".tmp", path, true); }
}
