using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SignalAtlas.Core;

namespace SignalAtlas.Collectors;

public static class ProcessTool
{
    public static async Task<(int ExitCode,string Output,string Error)> RunAsync(string file,IEnumerable<string> args,TimeSpan timeout,CancellationToken cancellationToken)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);linked.CancelAfter(timeout);
        var psi=new ProcessStartInfo(file){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args)psi.ArgumentList.Add(arg);
        using var process=Process.Start(psi)??throw new InvalidOperationException($"Failed to start {file}");
        try
        {
            var stdout=process.StandardOutput.ReadToEndAsync(linked.Token);
            var stderr=process.StandardError.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token);
            return(process.ExitCode,await stdout,await stderr);
        }
        catch(OperationCanceledException){try{process.Kill(entireProcessTree:true);}catch{}throw;}
    }
    public static string? FindExecutable(string name)
    {
        foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
        {
            if(string.IsNullOrWhiteSpace(dir)||!Path.IsPathRooted(dir))continue;
            foreach(var ext in new[]{".exe",".com"}){var path=Path.Combine(dir,name+ext);if(File.Exists(path))return path;}
        }
        if(name=="lms") {var p=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".lmstudio","bin","lms.exe");if(File.Exists(p))return p;}
        return null;
    }
    public static (string File,string[] Prefix)? FindDokoBot()
    {
        var node=FindExecutable("node");
        var prefixes=(Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)
            .Where(x=>!string.IsNullOrWhiteSpace(x)&&Path.IsPathRooted(x)).Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"npm"));
        foreach(var prefix in prefixes){var script=Path.Combine(prefix,"node_modules","@dokobot","cli","dist","cli","bin","dokobot.js");if(node is not null && File.Exists(script))return(node,[script]);}
        return null;
    }
}

public sealed class WebCollector : ICollector
{
    private readonly HttpClient _http;
    public bool BlockLinkedInReads {get;set;}=true;
    public bool UseDokoDiscovery {get;set;}=true;
    public string Type => "search";
    public WebCollector(HttpClient? http=null){_http=http??new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(45)};_http.DefaultRequestHeaders.UserAgent.ParseAdd("SignalAtlas/1.0 (+local research)");}
    public async Task<IReadOnlyList<Discovery>> DiscoverAsync(Topic topic,Source source,CancellationToken cancellationToken)
    {
        if(source.Type is "manual" or "linkedin_capture")return [];
        if(source.Type=="direct_web")
        {
            if(source.BaseUri is null || !Uri.TryCreate(source.BaseUri,UriKind.Absolute,out var direct) || !SourcePolicy.MayFetch(direct,source,BlockLinkedInReads))return [];
            return [new Discovery(source.Id,topic.Id,source.BaseUri,ContentTools.Canonicalize(source.BaseUri),source.Name,topic.Name,null,DateTimeOffset.UtcNow,true,1)];
        }
        if(source.Type=="search" && source.BaseUri is not null && Uri.TryCreate(source.BaseUri,UriKind.Absolute,out var linkedBase) && SourcePolicy.IsLinkedIn(linkedBase))
            return await DiscoverLinkedInAsync(topic,source,cancellationToken);
        string url;
        if(source.Type=="rss") {if(source.BaseUri is null) return [];url=source.BaseUri;}
        else
        {
            string query=ContentTools.Query(topic);
            if(source.PolicyMode=="discovery_only" && source.BaseUri is not null && Uri.TryCreate(source.BaseUri,UriKind.Absolute,out var b) && SourcePolicy.IsLinkedIn(b))query="site:linkedin.com/posts "+query;
            string endpoint=source.BaseUri??"https://www.bing.com/search?format=rss";
            url=endpoint+(endpoint.Contains('?')?"&":"?")+"q="+Uri.EscapeDataString(query);
        }
        var list=new List<Discovery>();
        int pages=source.Type=="rss"?1:Math.Min(5,(topic.MaxResultsPerRun+9)/10);
        for(int page=0;page<pages;page++)
        {
            string pageUrl=page==0?url:url+"&first="+(page*10+1);
            using var response=await _http.GetAsync(pageUrl,HttpCompletionOption.ResponseHeadersRead,cancellationToken);response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength is > 2_000_000)throw new InvalidDataException("Feed too large");
            string xml=await response.Content.ReadAsStringAsync(cancellationToken);if(xml.Length>2_000_000)throw new InvalidDataException("Feed too large");
            var feed=XDocument.Parse(xml);var rows=feed.Descendants().Where(e=>e.Name.LocalName is "item" or "entry");
            int before=list.Count;
            foreach(var row in rows)
            {
                string? link=row.Elements().FirstOrDefault(e=>e.Name.LocalName=="link")?.Attribute("href")?.Value ?? row.Elements().FirstOrDefault(e=>e.Name.LocalName=="link")?.Value;
                if(link is null || !Uri.TryCreate(link,UriKind.Absolute,out var uri) || uri.Scheme is not ("http" or "https"))continue;
                if(source.PolicyMode=="discovery_only" && source.BaseUri is not null && Uri.TryCreate(source.BaseUri,UriKind.Absolute,out var b) && SourcePolicy.IsLinkedIn(b) && !SourcePolicy.IsLinkedIn(uri))continue;
                string canonical=ContentTools.Canonicalize(link);if(list.Any(x=>x.CanonicalUrl==canonical))continue;
                string title=ContentTools.Normalize(row.Elements().FirstOrDefault(e=>e.Name.LocalName=="title")?.Value??link);
                string snippet=ContentTools.Normalize(row.Elements().FirstOrDefault(e=>e.Name.LocalName is "description" or "summary")?.Value??"");
                double score=ContentTools.Score(topic,title,snippet,link);if(score<=0)continue;
                var pub=row.Elements().FirstOrDefault(e=>e.Name.LocalName is "pubDate" or "published" or "updated")?.Value;
                DateTimeOffset? date=DateTimeOffset.TryParse(pub,out var parsed)?parsed.ToUniversalTime():null;
                if(date is not null && date<DateTimeOffset.UtcNow.AddHours(-topic.MaxAgeHours))continue;
                list.Add(new Discovery(source.Id,topic.Id,link,canonical,title,snippet,date,DateTimeOffset.UtcNow,SourcePolicy.MayFetch(uri,source,BlockLinkedInReads),score));
                if(list.Count>=topic.MaxResultsPerRun)break;
            }
            if(list.Count>=topic.MaxResultsPerRun || (source.Type!="rss" && list.Count==before))break;
        }
        if(list.Count==0 && source.Type=="search" && UseDokoDiscovery && ProcessTool.FindDokoBot() is not null)
        {
            string searchUrl="https://www.google.com/search?q="+Uri.EscapeDataString(ContentTools.Query(topic))+"&num=10";
            string? raw=await ReadWithDokoRawAsync(searchUrl,cancellationToken);
            if(raw is not null)
            foreach(var row in ParseDokoPublicSearch(raw,topic).Take(topic.MaxResultsPerRun))
            {
                var uri=new Uri(row.Url);
                list.Add(new Discovery(source.Id,topic.Id,row.Url,ContentTools.Canonicalize(row.Url),row.Title,row.Snippet,
                    null,DateTimeOffset.UtcNow,SourcePolicy.MayFetch(uri,source,BlockLinkedInReads),ContentTools.Score(topic,row.Title,row.Snippet,row.Url)));
            }
        }
        return list;
    }
    public static IReadOnlyList<(string Url,string Title,string Snippet)> ParseDokoPublicSearch(string raw,Topic topic)
    {
        var lines=raw.Replace("\r","").Split('\n');
        int references=Array.FindIndex(lines,x=>Regex.IsMatch(x,@"^\[\d+\]\s+https?://"));
        if(references<0)return [];
        var results=new List<(string Url,string Title,string Snippet)>();
        foreach(Match match in Regex.Matches(raw,@"(?m)^\[(\d+)\]\s+(https?://\S+)",RegexOptions.IgnoreCase))
        {
            string url=match.Groups[2].Value.TrimEnd(')',']',',');
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https") ||
                uri.Host.Equals("google.com",StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".google.com",StringComparison.OrdinalIgnoreCase))continue;
            string marker="["+match.Groups[1].Value+"]";
            var titles=lines.Take(references).Where(x=>x.Contains(marker,StringComparison.Ordinal) && !x.StartsWith(marker,StringComparison.Ordinal))
                .Select(x=>x.Replace(marker,"").Trim(' ','>','-')).Where(x=>x.Length>=8)
                .OrderByDescending(x=>ContentTools.Score(topic,x,"",url)).ThenByDescending(x=>x.Length).ToArray();
            if(titles.Length==0)continue;
            string title=titles[0];
            int index=Array.FindIndex(lines,0,references,x=>x.Contains(title,StringComparison.Ordinal));
            string snippet=index<0?"":string.Join(" ",lines.Skip(index+1).Take(4).Where(x=>x.Length>0 && x!="---"));
            if(ContentTools.Score(topic,title,snippet,url)<=0)continue;
            results.Add((url,title,snippet.Length>400?snippet[..400]:snippet));
        }
        return results.DistinctBy(x=>ContentTools.Canonicalize(x.Url)).ToArray();
    }
    private async Task<IReadOnlyList<Discovery>> DiscoverLinkedInAsync(Topic topic,Source source,CancellationToken token)
    {
        var found=new List<Discovery>();
        string query="site:linkedin.com "+ContentTools.Query(topic);
        if(UseDokoDiscovery && ProcessTool.FindDokoBot() is not null)
        {
            try
            {
                for(int page=0;page<Math.Min(3,(topic.MaxResultsPerRun+9)/10);page++)
                {
                    string url="https://www.google.com/search?q="+Uri.EscapeDataString(query)+"&num=10&start="+(page*10);
                    string? raw=await ReadWithDokoRawAsync(url,token);if(raw is null)break;
                    foreach(var row in ParseDokoLinkedInSearch(raw))
                    {
                        string canonical=ContentTools.Canonicalize(row.Url);if(found.Any(x=>x.CanonicalUrl==canonical))continue;
                        double score=ContentTools.Score(topic,row.Title,row.Snippet,row.Url);if(score<=0)continue;
                        var uri=new Uri(row.Url);
                        found.Add(new Discovery(source.Id,topic.Id,row.Url,canonical,row.Title,row.Snippet,null,DateTimeOffset.UtcNow,SourcePolicy.MayFetch(uri,source,BlockLinkedInReads),score));
                        if(found.Count>=topic.MaxResultsPerRun)return found;
                    }
                }
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
            catch{}
        }
        if(found.Count>0)return found;
        string rss="https://www.bing.com/search?format=rss&q="+Uri.EscapeDataString(query);
        try
        {
            using var response=await _http.GetAsync(rss,HttpCompletionOption.ResponseHeadersRead,token);
            if(!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is >2_000_000)return found;
            string xml=await response.Content.ReadAsStringAsync(token);if(xml.Length>2_000_000)return found;
            foreach(var row in XDocument.Parse(xml).Descendants().Where(x=>x.Name.LocalName=="item"))
            {
                string? url=row.Elements().FirstOrDefault(x=>x.Name.LocalName=="link")?.Value;
                if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||!SourcePolicy.IsLinkedIn(uri))continue;
                string canonical=ContentTools.Canonicalize(url!);if(found.Any(x=>x.CanonicalUrl==canonical))continue;
                string title=ContentTools.Normalize(row.Elements().FirstOrDefault(x=>x.Name.LocalName=="title")?.Value??url!);
                string snippet=ContentTools.Normalize(row.Elements().FirstOrDefault(x=>x.Name.LocalName=="description")?.Value??"");
                double score=ContentTools.Score(topic,title,snippet,url!);if(score<=0)continue;
                found.Add(new Discovery(source.Id,topic.Id,url!,canonical,title,snippet,null,DateTimeOffset.UtcNow,SourcePolicy.MayFetch(uri,source,BlockLinkedInReads),score));
                if(found.Count>=topic.MaxResultsPerRun)break;
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch{}
        return found;
    }
    public static IReadOnlyList<(string Url,string Title,string Snippet)> ParseDokoLinkedInSearch(string raw)
    {
        var lines=raw.Replace("\r","").Split('\n');
        var results=new List<(string,string,string)>();
        foreach(Match match in Regex.Matches(raw,@"(?m)^\[(\d+)\]\s+(https?://(?:www\.)?linkedin\.com/\S+)",RegexOptions.IgnoreCase))
        {
            string id=match.Groups[1].Value,url=match.Groups[2].Value.TrimEnd(')',']',',');
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||!SourcePolicy.IsLinkedIn(uri))continue;
            string marker="["+id+"]";
            int index=Array.FindIndex(lines,x=>x.Contains(marker) && !x.StartsWith(marker));
            string title=index>=0?Regex.Replace(lines[index],@"\s*\[\d+\].*$","").Trim(' ','>','-'):uri.AbsolutePath.Replace('-',' ');
            if(title.Length<8 || title.StartsWith("LinkedIn ·",StringComparison.OrdinalIgnoreCase))
            {
                int next=Array.FindIndex(lines,index+1,x=>x.Contains(marker) && !x.StartsWith(marker) && !x.StartsWith("LinkedIn ·"));
                if(next>=0){title=Regex.Replace(lines[next],@"\s*\[\d+\].*$","").Trim(' ','>','-');index=next;}
            }
            string snippet=index>=0?string.Join(" ",lines.Skip(index+1).Take(4).Where(x=>!x.StartsWith("---") && !x.StartsWith("LinkedIn ·"))).Trim():"";
            results.Add((url,title.Length>0?title:url,snippet.Length>400?snippet[..400]:snippet));
        }
        return results.DistinctBy(x=>x.Item1).ToArray();
    }
    public async Task<string?> FetchAsync(Discovery discovery,CancellationToken cancellationToken)
    {
        if(!discovery.FetchAllowed)return null;
        var uri=new Uri(discovery.CanonicalUrl);if(SourcePolicy.IsLinkedIn(uri) && BlockLinkedInReads)return null;
        if(!await IsPublicHost(uri,cancellationToken))return null;
        var doko=ProcessTool.FindDokoBot();
        if(doko is not null)
        {
            try
            {
                string? result=await ReadWithDokoAsync(discovery.CanonicalUrl,cancellationToken);
                if(result is {Length:>40})return result;
            }
            catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested){}
            catch(Exception) when(!SourcePolicy.IsLinkedIn(uri)){}
        }
        if(SourcePolicy.IsLinkedIn(uri))return null;
        using var response=await _http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        if(!response.IsSuccessStatusCode)return null;
        if(response.Content.Headers.ContentLength is > 3_000_000)return null;
        var media=response.Content.Headers.ContentType?.MediaType;
        if(media is not ("text/html" or "text/plain" or "application/xhtml+xml"))return null;
        string raw=await response.Content.ReadAsStringAsync(cancellationToken);if(raw.Length>3_000_000)return null;
        raw=Regex.Replace(raw,@"(?is)<(script|style|nav|footer|header|aside|form)[^>]*>.*?</\1>"," ");
        raw=Regex.Replace(raw,@"(?s)<[^>]+>"," ");return ContentTools.Normalize(raw);
    }
    public async Task<string> CaptureChosenPageAsync(string url,bool blockLinkedInCapture,CancellationToken cancellationToken)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||!SourcePolicy.MayUserInitiatedBrowserCapture(uri,blockLinkedInCapture,true))
            throw new InvalidOperationException("User-initiated browser capture is blocked for this URL.");
        if(!await IsPublicHost(uri,cancellationToken))throw new InvalidOperationException("The URL does not resolve to a public host.");
        string content=await ReadWithDokoAsync(url,cancellationToken)??throw new InvalidOperationException("DokoBot local read failed");
        if(content.Length<40)throw new InvalidDataException("DokoBot returned too little visible text to capture.");
        return content;
    }
    private static async Task<string?> ReadWithDokoAsync(string url,CancellationToken token)
    {
        var raw=await ReadWithDokoRawAsync(url,token);
        return raw is null?null:ContentTools.Normalize(raw);
    }
    private static async Task<string?> ReadWithDokoRawAsync(string url,CancellationToken token)
    {
        var doko=ProcessTool.FindDokoBot()??throw new FileNotFoundException("DokoBot local CLI is unavailable");
        var args=doko.Prefix.Concat(new[]{"read","--local","--timeout","90",url});
        var result=await ProcessTool.RunAsync(doko.File,args,TimeSpan.FromSeconds(100),token);
        var session=Regex.Match(result.Output+"\n"+result.Error,@"(?m)^Session:\s*(\d+)");
        if(session.Success)
        {
            try{await ProcessTool.RunAsync(doko.File,doko.Prefix.Concat(new[]{"close",session.Groups[1].Value}),TimeSpan.FromSeconds(10),CancellationToken.None);}catch{}
        }
        if(result.ExitCode!=0)return null;
        string body=RemoveSessionLine(result.Output);
        return body;
    }
    public static string RemoveSessionLine(string output)=>Regex.Replace(output,@"(?m)^Session:[^\r\n]*(?:\r?\n)?","");
    private static async Task<bool> IsPublicHost(Uri uri,CancellationToken token)
    {
        if(uri.Host.Equals("localhost",StringComparison.OrdinalIgnoreCase))return false;
        var ips=await Dns.GetHostAddressesAsync(uri.Host,token);
        return ips.Length>0 && ips.All(ip=>
        {
            var bytes=ip.GetAddressBytes();
            if(IPAddress.IsLoopback(ip)||ip.IsIPv6LinkLocal||ip.IsIPv6SiteLocal)return false;
            if(bytes.Length==4)return bytes[0]!=10 && bytes[0]!=127 && bytes[0]!=0 && !(bytes[0]==169&&bytes[1]==254) && !(bytes[0]==172&&bytes[1]>=16&&bytes[1]<=31) && !(bytes[0]==192&&bytes[1]==168);
            return !ip.IsIPv6UniqueLocal;
        });
    }
}
