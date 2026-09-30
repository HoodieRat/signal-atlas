using System.Net;
using System.Text.Json;
using SignalAtlas.Core;
using SignalAtlas.LmStudio;

namespace SignalAtlas.UnitTests;

public sealed class LmStudioTests
{
    [Fact] public async Task OwnedModelUsesStructuredInferenceAndUnloads()
    {
        string root=Path.Combine(Path.GetTempPath(),"SignalAtlasLmTest-"+Guid.NewGuid().ToString("N"));
        string owner=Path.Combine(root,"owner.json");bool loaded=false;int unloads=0;
        Task<(int ExitCode,string Output,string Error)> Cli(CancellationToken _,string[] args)
        {
            string command=string.Join(' ',args);
            if(command=="ls --json")return Task.FromResult((0,"[{\"type\":\"llm\",\"modelKey\":\"qwen/qwen3-4b-2507\",\"displayName\":\"Qwen 4B\",\"sizeBytes\":2300000000}]", ""));
            if(command=="ps --json")return Task.FromResult((0,loaded?"[{\"identifier\":\"signal-atlas\"}]":"[]",""));
            if(command.StartsWith("load --estimate-only"))return Task.FromResult((0,"Estimated GPU Memory: 1.50 GiB\nEstimated Total Memory: 2.00 GiB",""));
            if(command=="server status --json")return Task.FromResult((0,"{\"running\":true,\"port\":12345}",""));
            if(command.StartsWith("load ")){loaded=true;return Task.FromResult((0,"loaded",""));}
            if(command=="unload signal-atlas"){loaded=false;unloads++;return Task.FromResult((0,"unloaded",""));}
            throw new InvalidOperationException("Unexpected CLI call: "+command);
        }
        var handler=new FakeHandler();using var http=new HttpClient(handler);
        var probe=new FakeProbe();var backend=new LmStudioBackend(probe,12345,http,Cli,owner);
        try
        {
            Assert.True(await backend.PrepareAsync("qwen/qwen3-4b-2507",CancellationToken.None));
            Assert.True(File.Exists(owner));
            var topic=new Topic(1,"animation","",50,168,30,true,[]);
            var document=new Document(1,1,"https://example.com/post","Example","A detailed procedural animation article.","hash",null,DateTimeOffset.UtcNow,true,1,1);
            var analysis=await backend.AnalyzeAsync(document,topic,CancellationToken.None);
            Assert.Equal("A factual summary.",analysis?.Summary);
            Assert.Equal(85,analysis?.RelevanceScore);
            Assert.Contains("json_schema",handler.LastRequest);
            Assert.Contains("signal-atlas",handler.LastRequest);
            await backend.CleanupAsync(CancellationToken.None);
            Assert.Equal(1,unloads);Assert.False(loaded);Assert.False(File.Exists(owner));
        }
        finally{if(File.Exists(owner))File.Delete(owner);if(Directory.Exists(root))Directory.Delete(root);}
    }
    private sealed class FakeProbe:IResourceProbe
    {
        public ResourceSnapshot Sample(){const long g=1024L*1024*1024;return new(8*g,10*g,8*g,g,null,100,true);}
    }
    private sealed class FakeHandler:HttpMessageHandler
    {
        public string LastRequest="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            LastRequest=await request.Content!.ReadAsStringAsync(cancellationToken);
            string content=JsonSerializer.Serialize(new{summary="A factual summary.",relevance_score=85,novelty_score=75,relevance_reason="Matches animation",topics=new[]{"animation"},entities=Array.Empty<object>(),claims=Array.Empty<object>(),tags=Array.Empty<string>()});
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{choices=new[]{new{message=new{content}}}}))};
        }
    }
}
