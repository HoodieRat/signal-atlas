using System.Net;
using System.Text;
using SignalAtlas.Core;

namespace SignalAtlas.Reporting;

public sealed class CardDigestRenderer
{
    private static string E(string? value)=>WebUtility.HtmlEncode(value??"");
    private static string Link(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http"?E(uri.AbsoluteUri):"#";
    public string Render(RunSummary run,IReadOnlyList<ReportItem> items,string? synthesis)
    {
        var sb=new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Signal Atlas</title><style>");
        sb.Append("body{font:16px/1.55 system-ui,sans-serif;background:#f5f6f3;color:#202725;margin:0}header{background:#173c34;color:#fff;padding:3rem max(1rem,calc((100vw - 900px)/2))}main{max-width:900px;margin:2rem auto;padding:0 1rem}h1{font-size:2.2rem;margin:.3rem 0}h2{margin-top:2.5rem}.stats{display:flex;flex-wrap:wrap;gap:1.5rem}.stat strong{display:block;font-size:1.4rem}.card{background:#fff;border:1px solid #d9e1db;border-radius:12px;padding:1.2rem;margin:1rem 0;box-shadow:0 2px 8px #0000000a}.meta{font-size:.9rem;color:#51645b}.pill{background:#e3f0e7;border-radius:1rem;padding:.2rem .65rem}a{color:#14634a}footer{border-top:1px solid #ccd7ce;margin-top:3rem;padding:1.5rem 0;color:#536058}details{margin-top:2rem}p{white-space:pre-wrap}</style></head><body>");
        sb.Append("<header><small>SIGNAL ATLAS</small><h1>").Append(E(run.StartedUtc.ToLocalTime().ToString("dddd, MMMM d, yyyy"))).Append("</h1><div class=\"stats\">");
        Stat("Discovered",run.Discovered);Stat("New relevant",items.Count(x=>x.Document.IsNew));Stat("Analyzed",run.Analyzed);Stat("Duplicates",run.Duplicates);sb.Append("</div></header><main>");
        if(run.DeferredReason is not null)sb.Append("<div class=\"card\"><strong>Partial analysis</strong><p>").Append(E(run.DeferredReason)).Append(". Discovery results were preserved.</p></div>");
        sb.Append("<section><h2>What changed</h2><p>").Append(E(synthesis??"New discoveries are listed below with source links. AI synthesis was not available for this run.")).Append("</p></section>");
        sb.Append("<section><h2>Highest relevance</h2>");
        foreach(var item in items.Where(x=>x.Document.IsNew).OrderByDescending(x=>x.Analysis?.RelevanceScore??0).Take(12))Card(item);
        sb.Append("</section><section><h2>By topic</h2>");
        foreach(var group in items.GroupBy(x=>x.Topic.Name).OrderBy(x=>x.Key))
        {sb.Append("<h3>").Append(E(group.Key)).Append("</h3>");foreach(var item in group)Card(item);}
        sb.Append("</section>");
        var social=items.Where(x=>x.Document.Url is not null && x.Document.Url.Contains("linkedin.com",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(social.Length>0){sb.Append("<section><h2>Social activity</h2><p>").Append(social.Length).Append(" LinkedIn results or captures are linked to their original pages.</p></section>");}
        var previous=items.Where(x=>!x.Document.IsNew).ToArray();
        if(previous.Length>0){sb.Append("<details><summary>Previously seen (").Append(previous.Length).Append(")</summary>");foreach(var item in previous)Card(item);sb.Append("</details>");}
        sb.Append("<footer>Run ").Append(E(run.Id)).Append(" · Status: ").Append(E(run.Status)).Append(" · Model: ").Append(E(items.Select(x=>x.Analysis?.ModelKey).FirstOrDefault(x=>x is not null)??"not used")).Append(" · AI items: ").Append(run.Analyzed).Append(" · Duration: ").Append(E((DateTimeOffset.UtcNow-run.StartedUtc).ToString(@"mm\:ss"))).Append(" · Generated locally. Source content belongs to its respective owners.</footer></main></body></html>");
        return sb.ToString();
        void Stat(string label,int value)=>sb.Append("<div class=\"stat\"><strong>").Append(value).Append("</strong>").Append(E(label)).Append("</div>");
        void Card(ReportItem item)
        {
            sb.Append("<article class=\"card\"><div class=\"meta\">").Append(E(item.Source.Name)).Append(" · ").Append(E(item.Topic.Name)).Append(" · Published ").Append(E(item.Document.PublishedUtc?.ToLocalTime().ToString("MMM d, yyyy")??"unknown")).Append(" · Discovered ").Append(E(item.DiscoveredUtc?.ToLocalTime().ToString("MMM d, yyyy")??"unknown")).Append("</div><h3>").Append(E(item.Document.Title)).Append("</h3>");
            if(item.Analysis is not null)sb.Append("<span class=\"pill\">Relevance ").Append(item.Analysis.RelevanceScore.ToString("0")).Append("</span><p>").Append(E(item.Analysis.Summary)).Append("</p><p class=\"meta\">Why it matched: ").Append(E(item.Analysis.RelevanceReason)).Append("</p>");
            else sb.Append("<p>").Append(E(item.Document.Text.Length>500?item.Document.Text[..500]:item.Document.Text)).Append("</p><p class=\"meta\">Local AI analysis not yet available.</p>");
            sb.Append("<a href=\"").Append(Link(item.Document.Url)).Append("\" rel=\"noopener noreferrer\">Open original</a></article>");
        }
    }
    public static string Publish(string html,string runId)
    {
        AppPaths.Ensure();string archive=Path.Combine(AppPaths.Reports,"Reports",DateTimeOffset.Now.ToString("yyyy-MM-dd_HHmmss")+"_"+runId[..8]+".html");
        string temp=archive+".tmp";File.WriteAllText(temp,html,Encoding.UTF8);File.Move(temp,archive,true);
        string latest=Path.Combine(AppPaths.Reports,"Latest Report.html");File.Copy(archive,latest,true);return archive;
    }
}
