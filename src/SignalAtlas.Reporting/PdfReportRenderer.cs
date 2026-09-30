using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes.Charts;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using SignalAtlas.Core;
using MDocument = MigraDoc.DocumentObjectModel.Document;

namespace SignalAtlas.Reporting;

public sealed record RenderedPdf(byte[] Bytes, int Pages);

public sealed class PdfReportRenderer
{
    private static readonly object FontLock = new();
    private static readonly Color Ink = Color.FromRgb(27, 48, 57);
    private static readonly Color Green = Color.FromRgb(27, 93, 79);
    public RenderedPdf Render(ResearchReport report)
    {
        lock (FontLock) { GlobalFontSettings.UseWindowsFontsUnderWindows = true; }
        var doc = new MDocument();
        doc.Info.Title = report.Title;
        doc.Info.Author = "Signal Atlas";
        doc.Info.Subject = report.Options.Question;
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Times New Roman"; normal.Font.Size = report.Options.Pages <= 5 ? 10.5 : 11;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(report.Options.Pages <= 5 ? 5 : 7);
        normal.ParagraphFormat.WidowControl = true;
        normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Multiple;
        normal.ParagraphFormat.LineSpacing = 1.13;
        foreach (string name in new[] { StyleNames.Heading1, StyleNames.Heading2 })
        {
            var style = doc.Styles[name]!; style.Font.Name = "Arial"; style.Font.Color = Green;
            style.Font.Size = name == StyleNames.Heading1 ? (report.Options.Pages <= 5 ? 14 : 15) : 12; style.Font.Bold = true;
            style.ParagraphFormat.SpaceBefore = Unit.FromPoint(report.Options.Pages <= 5 ? 12 : 15); style.ParagraphFormat.SpaceAfter = Unit.FromPoint(7);
            style.ParagraphFormat.KeepWithNext = true;
        }
        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.TopMargin = Unit.FromInch(.75); section.PageSetup.BottomMargin = Unit.FromInch(.7);
        section.PageSetup.LeftMargin = Unit.FromInch(.8); section.PageSetup.RightMargin = Unit.FromInch(.8);
        section.PageSetup.HeaderDistance = Unit.FromInch(.3); section.PageSetup.FooterDistance = Unit.FromInch(.3);
        var header = section.Headers.Primary.AddParagraph("SIGNAL ATLAS  /  " + report.Options.Profile.ToUpperInvariant());
        header.Format.Font.Name = "Arial"; header.Format.Font.Size = 8; header.Format.Font.Color = Green;
        header.Format.Borders.Bottom.Width = .6; header.Format.Borders.Bottom.Color = Green;
        header.Format.SpaceAfter = Unit.FromPoint(6);
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Name = "Arial"; footer.Format.Font.Size = 8; footer.Format.Font.Color = Colors.Gray;
        footer.Format.TabStops.AddTabStop(Unit.FromInch(6.9), TabAlignment.Right);
        footer.AddText(report.CreatedUtc.ToLocalTime().ToString("dd MMM yyyy") + "  |  Research report\t");
        footer.AddPageField(); footer.AddText(" / "); footer.AddNumPagesField();

        var title = section.AddParagraph(report.Title);
        title.Format.Font.Name = "Arial"; title.Format.Font.Size = report.Options.Pages >= 8 ? 29 : 23;
        title.Format.Font.Bold = true; title.Format.Font.Color = Ink;
        title.Format.SpaceBefore = Unit.FromPoint(report.Options.Pages >= 8 ? 65 : 8);
        title.Format.SpaceAfter = Unit.FromPoint(12); title.Format.KeepWithNext = true;
        var subtitle = section.AddParagraph(report.Subtitle); subtitle.Format.Font.Size = 12; subtitle.Format.Font.Color = Green;
        if (!string.IsNullOrWhiteSpace(report.Options.Question))
        {
            var question = section.AddParagraph("Research question: " + report.Options.Question);
            question.Format.Font.Italic = true;
        }
        if (report.Options.Pages >= 8)
        {
            var audience = section.AddParagraph("Prepared for: " + report.Options.Audience); audience.Format.Font.Size = 10;
            section.AddParagraph("Contents", StyleNames.Heading1);
            Toc("Executive summary", "executive");
            for (int i = 0; i < report.Chapters.Length; i++) Toc($"{i + 1}. {report.Chapters[i].Heading}", "chapter-" + i);
            Toc("Conclusions", "conclusion"); Toc("References and research scope", "references");
            section.AddPageBreak();
        }
        Heading("Executive summary", "executive");
        foreach (var p in report.ExecutiveSummary) Paragraph(p);
        int tableNumber = 0, figureNumber = 0;
        for (int i = 0; i < report.Chapters.Length; i++)
        {
            var chapter = report.Chapters[i];
            Heading($"{i + 1}. {chapter.Heading}", "chapter-" + i);
            foreach (var p in chapter.Paragraphs) Paragraph(p);
            foreach (var data in chapter.Tables)
            {
                var caption = section.AddParagraph($"Table {++tableNumber}. {data.Caption}");
                caption.Format.Font.Name = "Arial"; caption.Format.Font.Size = 9; caption.Format.Font.Bold = true; caption.Format.KeepWithNext = true;
                var table = section.AddTable(); table.Borders.Width = .3; table.Borders.Color = Color.FromRgb(204, 216, 213);
                table.Format.Font.Name = "Arial"; table.Format.Font.Size = 8.5; table.Format.SpaceAfter = Unit.FromPoint(4);
                table.TopPadding = Unit.FromPoint(5); table.BottomPadding = Unit.FromPoint(5);
                foreach (var _ in data.Columns) table.AddColumn(Unit.FromInch(6.9 / data.Columns.Length));
                var head = table.AddRow(); head.HeadingFormat = true; head.Format.Font.Bold = true;
                head.Shading.Color = Color.FromRgb(225, 235, 230);
                for (int c = 0; c < data.Columns.Length; c++) head.Cells[c].AddParagraph(data.Columns[c]);
                for (int r = 0; r < data.Rows.Length; r++)
                {
                    var row = table.AddRow(); if (r % 2 == 1) row.Shading.Color = Color.FromRgb(246, 248, 247);
                    for (int c = 0; c < data.Columns.Length; c++)
                    {
                        var p = row.Cells[c].AddParagraph(data.Rows[r].Cells[c]);
                        if (c == data.Columns.Length - 1) Cite(p, data.Rows[r].SourceIds);
                    }
                }
                section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(2);
            }
            foreach (var data in chapter.Charts)
            {
                var caption = section.AddParagraph($"Figure {++figureNumber}. {data.Caption} ({data.Unit})");
                caption.Format.Font.Name = "Arial"; caption.Format.Font.Size = 9; caption.Format.Font.Bold = true; caption.Format.KeepWithNext = true;
                var chart = section.AddChart(ChartType.Bar2D); chart.Width = Unit.FromInch(6.7); chart.Height = Unit.FromInch(2.6);
                chart.PlotArea.LeftPadding = Unit.FromPoint(8); chart.PlotArea.RightPadding = Unit.FromPoint(8);
                chart.PlotArea.TopPadding = Unit.FromPoint(8); chart.PlotArea.BottomPadding = Unit.FromPoint(8);
                chart.XValues.AddXSeries().Add(data.Values.Select(x => x.Label).ToArray());
                var series = chart.SeriesCollection.AddSeries(); series.Add(data.Values.Select(x => x.Value).ToArray());
                series.FillFormat.Color = Green; chart.Format.Font.Name = "Arial"; chart.Format.Font.Size = 9;
                chart.XAxis.TickLabels.Font.Name = "Arial"; chart.XAxis.TickLabels.Font.Size = 9;
                chart.YAxis.TickLabels.Font.Name = "Arial"; chart.YAxis.TickLabels.Font.Size = 9;
                chart.YAxis.TickLabels.Format = "0.##";
                chart.XAxis.LineFormat.Width = .5; chart.YAxis.LineFormat.Width = .5;
                chart.YAxis.MinimumScale = 0; chart.YAxis.MaximumScale = Math.Max(1, data.Values.Max(x => x.Value) * 1.15);
                chart.YAxis.MajorTick = chart.YAxis.MaximumScale / 4;
                chart.YAxis.HasMajorGridlines = true; chart.YAxis.MajorGridlines.LineFormat.Width = .3;
                chart.YAxis.MajorGridlines.LineFormat.Color = Color.FromRgb(210, 222, 216);
                var note = section.AddParagraph("Source measurements: ");
                note.Format.Font.Size = 8;
                foreach (var value in data.Values) { note.AddText($"{value.Label}: {value.Value:0.##} {data.Unit}"); Cite(note, [value.SourceId]); note.AddText("  "); }
            }
        }
        Heading("Conclusions", "conclusion");
        foreach (var p in report.Conclusion) Paragraph(p);
        Heading("References and research scope", "references");
        foreach (var source in report.Sources)
        {
            var p = section.AddParagraph(); p.Format.Font.Size = report.Options.Pages == 1 ? 7.5 : 8.5;
            p.Format.SpaceAfter = Unit.FromPoint(4); p.AddBookmark("source-" + source.Id);
            p.AddFormattedText($"[{source.Id}] ", TextFormat.Bold);
            if (Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                var link = p.AddHyperlink(uri.AbsoluteUri, HyperlinkType.Web); link.AddText(source.Title); link.Font.Color = Green;
            }
            else p.AddText(source.Title);
            p.AddText($". {source.Publisher}. Published {source.PublishedUtc?.ToString("dd MMM yyyy") ?? "date not stated"}; accessed {source.RetrievedUtc:dd MMM yyyy}.");
        }
        foreach (string note in report.CoverageNotes)
        {
            var p = section.AddParagraph(note); p.Format.Font.Size = 8; p.Format.Font.Color = Colors.Gray; p.Format.SpaceAfter = Unit.FromPoint(3);
        }
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        int pageCount=renderer.PdfDocument.PageCount;
        using var stream = new MemoryStream(); renderer.PdfDocument.Save(stream, false);
        return new(stream.ToArray(), pageCount);

        void Heading(string text, string bookmark) { var p = section.AddParagraph(text, StyleNames.Heading1); p.AddBookmark(bookmark); }
        void Paragraph(CitedParagraph content) { var p = section.AddParagraph(content.Text); Cite(p, content.SourceIds); }
        void Cite(Paragraph paragraph, IEnumerable<int> ids)
        {
            paragraph.AddText(" ");
            foreach (int id in ids.Distinct()) { var link = paragraph.AddHyperlink("source-" + id, HyperlinkType.Bookmark); link.AddText($"[{id}]"); link.Font.Color = Green; link.Font.Size = 8; }
        }
        void Toc(string text, string bookmark)
        {
            var p = section.AddParagraph(); p.Format.Font.Name = "Arial"; p.Format.Font.Size = 10;
            p.Format.TabStops.AddTabStop(Unit.FromInch(6.8), TabAlignment.Right, TabLeader.Dots);
            p.AddHyperlink(bookmark, HyperlinkType.Bookmark).AddText(text); p.AddTab(); p.AddPageRefField(bookmark);
        }
    }
}
