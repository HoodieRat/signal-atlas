using SignalAtlas.Core;
using SignalAtlas.Collectors;
using SignalAtlas.Data;
using SignalAtlas.Reporting;
using SignalAtlas.Resources;

namespace SignalAtlas.UnitTests;

public sealed class MonitorTests
{
    private static Source LinkedIn=>new(1,"LinkedIn","search","https://www.linkedin.com","discovery_only",true,null);
    [Fact] public void LinkedInPolicyDefaultsToBlockedAndOptOutAllowsScheduledFetch()
    {
        var uri=new Uri("https://www.linkedin.com/posts/example");
        Assert.False(SourcePolicy.MayFetch(uri,LinkedIn));
        Assert.True(SourcePolicy.MayFetch(uri,LinkedIn,false));
        Assert.False(SourcePolicy.MayUserInitiatedBrowserCapture(uri,true,true));
        Assert.True(SourcePolicy.MayUserInitiatedBrowserCapture(uri,false,true));
        Assert.False(SourcePolicy.MayUserInitiatedBrowserCapture(uri,false,false));
        Assert.True(SourcePolicy.IsValid(LinkedIn));
        Assert.False(SourcePolicy.IsValid(LinkedIn with {PolicyMode="automated_direct"}));
    }
    [Fact] public void CanonicalizationKeepsIdentityParametersAndRemovesTracking()
    {
        Assert.Equal("https://example.com/post?id=42",ContentTools.Canonicalize("http://EXAMPLE.com/post/?utm_source=mail&id=42#section"));
    }
    [Fact] public void TopicGateHonorsExclusionsAndQueriesAreDeterministic()
    {
        var topic=new Topic(1,"animation","",50,168,30,true,[new("procedural animation","exact"),new("jobs","exclude")]);
        Assert.Equal(0,ContentTools.Score(topic,"Procedural animation jobs",""));
        Assert.True(ContentTools.Score(topic,"Procedural animation demo","")>0);
        Assert.Equal("\"procedural animation\" -\"jobs\"",ContentTools.Query(topic));
    }
    [Fact] public void RequiredKeywordsAndDomainsAreEnforced()
    {
        var topic=new Topic(1,"animation","",50,168,30,true,[new("animation","include")],["procedural","rigging"],["example.com"]);
        Assert.Equal(0,ContentTools.Score(topic,"procedural animation","rigging","https://elsewhere.com/post"));
        Assert.Equal(0,ContentTools.Score(topic,"procedural animation","","https://example.com/post"));
        Assert.True(ContentTools.Score(topic,"procedural animation rigging","","https://blog.example.com/post")>0);
    }
    [Fact] public void HashAndSimilarityNormalizeContent()
    {
        Assert.Equal(ContentTools.Hash("A   new\npost"),ContentTools.Hash("a new post"));
        Assert.Equal(1,ContentTools.Similarity("alpha beta gamma delta epsilon","ALPHA beta gamma delta epsilon"));
    }
    [Fact] public void StateMachineRejectsBackwardsTransitions()
    {
        Assert.True(RunPhases.CanTransition("COLLECTING","NORMALIZING"));
        Assert.False(RunPhases.CanTransition("ANALYZING","COLLECTING"));
        Assert.False(RunPhases.CanTransition("COMPLETED","ANALYZING"));
    }
    [Fact] public void GovernorDefersUnderPressure()
    {
        const long g=1024L*1024*1024;
        Assert.Equal(ResourceDecision.Defer,ResourceGovernor.Decide(new ResourceSnapshot(3*g,10*g,8*g,2*g,null,100,true),2*g).Decision);
        Assert.Equal(ResourceDecision.Defer,ResourceGovernor.Decide(new ResourceSnapshot(8*g,10*g,null,null,null,100,true),2*g).Decision);
        Assert.Equal(ResourceDecision.Normal,ResourceGovernor.Decide(new ResourceSnapshot(8*g,10*g,8*g,g,null,100,true),2*g).Decision);
        Assert.Equal(ResourceDecision.Reduced,ResourceGovernor.Decide(new ResourceSnapshot(8*g,10*g,8*g,g,90,100,true),2*g).Decision);
    }
    [Fact] public void ReportEncodesUntrustedContent()
    {
        var t=new Topic(1,"Test","",50,168,30,true,[]);var s=new Source(1,"source","search",null,"automated_direct",true,null);
        var d=new Document(1,1,"https://example.com/?id=1&x=2","<script>alert(1)</script>","","hash",null,DateTimeOffset.UtcNow,true,1,1);
        var run=new RunSummary("run","COMPLETED","COMPLETED",1,1,0,0,0,null,DateTimeOffset.UtcNow,null);
        var analysis=new Analysis(1,"test",80,50,"<img src=x>","match","{}");
        var brief=new ResearchBrief("<img src=x>",[new ReportFinding("Finding","<script>alert(1)</script>",[1])],""," ");
        string html=new HtmlReportRenderer().Render(run,[new ReportItem(d,t,s,analysis)],brief);
        Assert.DoesNotContain("<script>alert(1)</script>",html);
        Assert.Contains("&lt;script&gt;",html);
        Assert.Contains("&lt;img src=x&gt;",html);
        Assert.Contains("id=1&amp;x=2",html);
        Assert.Contains("class=\"card\"",html);
    }
    [Fact] public void DiscoveryOnlyRunIsLabeledAsLog()
    {
        var t=new Topic(1,"Test","",50,168,30,true,[]);
        var s=new Source(1,"source","search",null,"discovery_only",true,null);
        var d=new Document(1,1,"https://example.com","Example","Snippet","hash",null,DateTimeOffset.UtcNow,true,1,1);
        var run=new RunSummary("run","COMPLETED","COMPLETED",1,0,0,0,0,null,DateTimeOffset.UtcNow,null);
        string html=new HtmlReportRenderer().Render(run,[new ReportItem(d,t,s,null)],null,true);
        Assert.Contains("Discovery-only run",html);
        Assert.Contains("class=\"card\"",html);
        Assert.DoesNotContain("<h2>Key findings</h2>",html);
    }
    [Fact] public void MigrationAndChosenContentPathPreserveFullText()
    {
        string root=Path.Combine(Path.GetTempPath(),"SignalAtlasTest-"+Guid.NewGuid().ToString("N"));
        try
        {
            var db=new ResearchDatabase(Path.Combine(root,"research.db"),Path.Combine(root,"documents"));db.Initialize();db.Initialize();
            Assert.Equal("LinkedIn setting controlled",db.Sources().First(x=>x.Name=="LinkedIn Search Discovery").DisplayPolicy);
            long schedule=db.SaveSchedule(new MonitorSchedule(0,"Discovery",true,"08:00",["Monday"],true,false,30,[],"discovery_only"));
            Assert.Equal("discovery_only",db.Schedules().First(x=>x.Id==schedule).ReportMode);
            long topic=db.AddTopic("animation");
            long search=db.Sources().First(x=>x.Type=="search").Id;
            db.SetTopicSources(topic,new Dictionary<long,bool>{{search,false}});
            Assert.False(db.IsSourceEnabledForTopic(topic,search));
            string id=db.ImportChosenContent(topic,"https://www.linkedin.com/posts/example","Example","A useful selected post about procedural animation with visible context and specific details.");
            Assert.Single(db.Pending(id));
            Assert.Contains("specific details",db.Pending(id)[0].Document.Text);
            Assert.Single(db.ReportItems(id));
            string reportPath=Path.Combine(root,"report.html");db.SaveReport(id,reportPath,null,false);
            var entry=Assert.Single(db.ReportEntries(reportPath));db.SetBookmark(reportPath,entry.DocumentId,entry.TopicId,true);
            Assert.True(Assert.Single(db.ReportEntries(reportPath)).Bookmarked);
            using var connection=db.Open();Assert.Equal("ok",ResearchDatabase.Scalar<string>(connection,"PRAGMA integrity_check"));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact] public void SchedulerXmlContainsMultipleTriggersAndIgnoreNew()
    {
        var db=new ResearchDatabase(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db"));
        var service=new TaskSchedulerService(db,@"C:\Program Files\SignalAtlas.Runner.exe");
        string xml=service.BuildXml([new MonitorSchedule(1,"AM",true,"08:00",["Monday","Tuesday"],true,false,45),new MonitorSchedule(2,"PM",true,"20:00",["Wednesday"],true,false,45)]);
        Assert.Equal(2,System.Xml.Linq.XDocument.Parse(xml).Descendants().Count(x=>x.Name.LocalName=="CalendarTrigger"));
        Assert.Contains("IgnoreNew",xml);Assert.Contains("--scheduled",xml);
        Assert.Contains("--schedule-id 1",service.BuildXml([new MonitorSchedule(1,"AM",true,"08:00",["Monday"],true,false,45)],1));
    }
    [Fact] public void DokoSearchParserKeepsLinkedInResultsOnly()
    {
        const string raw="Some result title about procedural animation [4]\nLinkedIn · Person [4]\nBrief context\n[4] https://www.linkedin.com/posts/person_procedural-animation-activity-123\n[5] https://example.com/other";
        var rows=WebCollector.ParseDokoLinkedInSearch(raw);
        Assert.Single(rows);Assert.Contains("procedural animation",rows[0].Title);
    }
    [Fact] public void PublicSearchFallbackKeepsMatchingExternalResults()
    {
        const string raw="Account [2]\nProcedural animation [24]\nAnimation driven by algorithms.\nOther result [25]\n[2] https://accounts.google.com/\n[24] https://en.wikipedia.org/wiki/Procedural_animation\n[25] https://example.com/other";
        var topic=new Topic(1,"procedural animation","",50,168,30,true,[new("procedural animation","include")]);
        var rows=WebCollector.ParseDokoPublicSearch(raw,topic);
        Assert.Single(rows);Assert.Equal("https://en.wikipedia.org/wiki/Procedural_animation",rows[0].Url);
    }
    [Fact] public void DokoSessionCleanupPreservesPageText()
    {
        string output="Session: 42\r\nA visible LinkedIn post\r\nAuthor and relevant context\r\n";
        string cleaned=WebCollector.RemoveSessionLine(output);
        Assert.DoesNotContain("Session: 42",cleaned);
        Assert.Contains("A visible LinkedIn post",cleaned);
        Assert.Contains("Author and relevant context",cleaned);
    }
    [Fact] public async Task LinkedInDiscoveryUsesPublicSearchWhenDokoIsUnavailable()
    {
        const string feed="<rss><channel><item><title>Procedural animation at studio</title><link>https://www.linkedin.com/posts/studio_procedural-animation-123</link><description>New procedural animation workflow</description></item><item><title>Other result</title><link>https://example.com/post</link></item></channel></rss>";
        using var client=new HttpClient(new FixtureHandler(feed));
        var collector=new WebCollector(client){UseDokoDiscovery=false,BlockLinkedInReads=true};
        var topic=new Topic(1,"procedural animation","",50,168,30,true,[new("procedural animation","include")]);
        var results=await collector.DiscoverAsync(topic,LinkedIn,CancellationToken.None);
        Assert.Single(results);Assert.False(results[0].FetchAllowed);
        collector.BlockLinkedInReads=false;
        Assert.True((await collector.DiscoverAsync(topic,LinkedIn,CancellationToken.None))[0].FetchAllowed);
    }
    private sealed class FixtureHandler(string body):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
            =>Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(body)});
    }
    [Fact] public void InterruptedPendingAnalysisMovesToNextRunOnce()
    {
        string root=Path.Combine(Path.GetTempPath(),"SignalAtlasRecovery-"+Guid.NewGuid().ToString("N"));
        try
        {
            var db=new ResearchDatabase(Path.Combine(root,"research.db"),Path.Combine(root,"documents"));db.Initialize();long topic=db.AddTopic("animation");
            string old=db.ImportChosenContent(topic,"https://www.linkedin.com/posts/recovery","Recovery test","A selected post about procedural animation with enough visible text to analyze safely.");
            db.Finish(old,"PARTIAL","Worker interrupted");string current=Guid.NewGuid().ToString("N");db.CreateRun(current,"scheduled");
            db.RecoverPendingToRun(current);db.RecoverPendingToRun(current);
            Assert.Single(db.Pending(current));Assert.Empty(db.Pending(old));Assert.Single(db.ReportItems(current));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
