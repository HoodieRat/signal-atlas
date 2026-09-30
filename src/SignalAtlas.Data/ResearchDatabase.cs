using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SignalAtlas.Core;

namespace SignalAtlas.Data;

public sealed record BookmarkEntry(long DocumentId,long TopicId,string Title,string Topic,bool Bookmarked);

public sealed class ResearchDatabase
{
    public string Path { get; }
    public string DocumentRoot { get; }
    public ResearchDatabase(string? path = null,string? documentRoot=null) { Path = path ?? AppPaths.Database; DocumentRoot=documentRoot??AppPaths.Documents; }
    public SqliteConnection Open()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = true }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
        return c;
    }
    public void Initialize()
    {
        using var c = Open();
        using (var cmd = c.CreateCommand()) { cmd.CommandText = "CREATE TABLE IF NOT EXISTS schema_info(version INTEGER NOT NULL, applied_utc TEXT NOT NULL)"; cmd.ExecuteNonQuery(); }
        long version = Scalar<long>(c, "SELECT COALESCE(MAX(version),0) FROM schema_info");
        var names = Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(x => x.EndsWith(".sql")).OrderBy(x => x);
        foreach (var name in names)
        {
            int migration = int.Parse(name.Split('.').Reverse().Skip(1).First().Split('_')[0]);
            if (migration <= version) continue;
            using var tx = c.BeginTransaction();
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = reader.ReadToEnd(); cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO schema_info(version,applied_utc) VALUES($v,$now)";
            cmd.Parameters.AddWithValue("$v", migration); cmd.Parameters.AddWithValue("$now", Now()); cmd.ExecuteNonQuery();
            tx.Commit(); version = migration;
        }
        Seed();
    }
    public void Seed()
    {
        using var c = Open();
        if (Scalar<long>(c, "SELECT COUNT(*) FROM sources") == 0)
        {
            AddSource("General Web Search", "search", "https://www.bing.com/search?format=rss", "automated_direct");
            AddSource("LinkedIn Search Discovery", "search", "https://www.linkedin.com", "discovery_only");
            AddSource("Manual Imports", "manual", null, "manual_import_only");
        }
        if(Scalar<long>(c,"SELECT COUNT(*) FROM sources WHERE source_type='linkedin_capture'")==0)
            AddSource("LinkedIn Chosen Content","linkedin_capture","https://www.linkedin.com","manual_import_only");
        if (Scalar<long>(c, "SELECT COUNT(*) FROM model_profiles") == 0)
        {
            Execute(c, "INSERT INTO model_profiles(display_name,model_key,is_fallback) VALUES('Qwen3-4B-Instruct-2507','qwen/qwen3-4b-2507',1)");
            Execute(c, "INSERT INTO model_profiles(display_name,model_key,is_fallback) VALUES('Qwen3.5-4B','qwen/qwen3.5-4b',0)");
        }
    }
    public static string Now() => DateTimeOffset.UtcNow.ToString("O");
    public static void Execute(SqliteConnection c, string sql, params (string Key, object? Value)[] args)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        foreach (var (key, value) in args) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    public static T Scalar<T>(SqliteConnection c, string sql, params (string Key, object? Value)[] args)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        foreach (var (key, value) in args) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return (T)Convert.ChangeType(cmd.ExecuteScalar() ?? default(T)!, typeof(T));
    }
    public long AddTopic(string name, string description = "", int priority = 50, int maxAgeHours = 168, int maxResults = 30)
    {
        using var c = Open();
        Execute(c, "INSERT INTO topics(name,description,priority,max_age_hours,max_results_per_run,created_utc,updated_utc) VALUES($n,$d,$p,$a,$m,$t,$t)", ("$n",name),("$d",description),("$p",priority),("$a",maxAgeHours),("$m",maxResults),("$t",Now()));
        return Scalar<long>(c, "SELECT last_insert_rowid()");
    }
    public void SetTopic(long id, string name, bool enabled, int priority, int maxAgeHours, int maxResults)
    {
        using var c = Open(); Execute(c,"UPDATE topics SET name=$n,enabled=$e,priority=$p,max_age_hours=$a,max_results_per_run=$m,updated_utc=$t WHERE id=$id",("$n",name),("$e",enabled?1:0),("$p",priority),("$a",maxAgeHours),("$m",maxResults),("$t",Now()),("$id",id));
    }
    public void SetTopicRules(long id,IReadOnlyList<string> allKeywords,IReadOnlyList<string> domains)
    {
        using var c=Open();Execute(c,"UPDATE topics SET all_keywords_json=$all,allowed_domains_json=$domains,updated_utc=$t WHERE id=$id",("$all",JsonSerializer.Serialize(allKeywords)),("$domains",JsonSerializer.Serialize(domains)),("$t",Now()),("$id",id));
    }
    public void AddTerm(long topicId, string term, string type, double weight = 1)
    {
        if (type is not ("include" or "exclude" or "synonym" or "exact")) throw new ArgumentException("Invalid term type");
        using var c=Open(); Execute(c,"INSERT INTO topic_terms(topic_id,term,term_type,weight) VALUES($t,$v,$y,$w)",("$t",topicId),("$v",term),("$y",type),("$w",weight));
    }
    public void ReplaceTerms(long topicId,IReadOnlyList<TopicTerm> terms)
    {
        using var c=Open();using var tx=c.BeginTransaction();
        using(var delete=c.CreateCommand()){delete.Transaction=tx;delete.CommandText="DELETE FROM topic_terms WHERE topic_id=$id";delete.Parameters.AddWithValue("$id",topicId);delete.ExecuteNonQuery();}
        foreach(var term in terms){if(term.Type is not("include" or "exclude" or "exact" or "synonym"))throw new ArgumentException("Invalid term type");using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO topic_terms(topic_id,term,term_type,weight) VALUES($id,$term,$type,$weight)";cmd.Parameters.AddWithValue("$id",topicId);cmd.Parameters.AddWithValue("$term",term.Term);cmd.Parameters.AddWithValue("$type",term.Type);cmd.Parameters.AddWithValue("$weight",term.Weight);cmd.ExecuteNonQuery();}
        tx.Commit();
    }
    public IReadOnlyList<Topic> Topics(bool enabledOnly = false)
    {
        using var c=Open(); using var cmd=c.CreateCommand(); cmd.CommandText="SELECT id,name,COALESCE(description,''),priority,max_age_hours,max_results_per_run,enabled,all_keywords_json,allowed_domains_json FROM topics"+(enabledOnly?" WHERE enabled=1":"")+" ORDER BY priority DESC,name";
        using var r=cmd.ExecuteReader(); var list=new List<Topic>();
        while(r.Read()) { long id=r.GetInt64(0); list.Add(new Topic(id,r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetInt32(4),r.GetInt32(5),r.GetInt32(6)!=0,Terms(id),r.IsDBNull(7)?[]:JsonSerializer.Deserialize<string[]>(r.GetString(7)),r.IsDBNull(8)?[]:JsonSerializer.Deserialize<string[]>(r.GetString(8)))); }
        return list;
    }
    private IReadOnlyList<TopicTerm> Terms(long id)
    {
        using var c=Open(); using var cmd=c.CreateCommand(); cmd.CommandText="SELECT term,term_type,weight FROM topic_terms WHERE topic_id=$id";cmd.Parameters.AddWithValue("$id",id);
        using var r=cmd.ExecuteReader();var list=new List<TopicTerm>();while(r.Read())list.Add(new(r.GetString(0),r.GetString(1),r.GetDouble(2)));return list;
    }
    public long AddSource(string name,string type,string? baseUri,string policyMode,string? configJson=null)
    {
        var source=new Source(0,name,type,baseUri,policyMode,true,configJson);if(!SourcePolicy.IsValid(source))throw new InvalidOperationException("Source policy violation");
        using var c=Open();Execute(c,"INSERT INTO sources(name,source_type,base_uri,policy_mode,config_json,created_utc,updated_utc) VALUES($n,$y,$b,$p,$j,$t,$t)",("$n",name),("$y",type),("$b",baseUri),("$p",policyMode),("$j",configJson),("$t",Now()));return Scalar<long>(c,"SELECT last_insert_rowid()");
    }
    public IReadOnlyList<Source> Sources(bool enabledOnly=false)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT id,name,source_type,base_uri,policy_mode,enabled,config_json FROM sources"+(enabledOnly?" WHERE enabled=1":"")+" ORDER BY name";using var r=cmd.ExecuteReader();var list=new List<Source>();while(r.Read())list.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.GetString(4),r.GetInt32(5)!=0,r.IsDBNull(6)?null:r.GetString(6)));return list;
    }
    public void SetSourceEnabled(long id,bool enabled){using var c=Open();Execute(c,"UPDATE sources SET enabled=$e,updated_utc=$t WHERE id=$id",("$e",enabled?1:0),("$t",Now()),("$id",id));}
    public IReadOnlyDictionary<long,bool> TopicSourceSelections(long topicId)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT source_id,enabled FROM topic_sources WHERE topic_id=$id";cmd.Parameters.AddWithValue("$id",topicId);using var r=cmd.ExecuteReader();var result=new Dictionary<long,bool>();while(r.Read())result[r.GetInt64(0)]=r.GetInt32(1)!=0;return result;
    }
    public void SetTopicSources(long topicId,IReadOnlyDictionary<long,bool> selections)
    {
        using var c=Open();using var tx=c.BeginTransaction();using(var delete=c.CreateCommand()){delete.Transaction=tx;delete.CommandText="DELETE FROM topic_sources WHERE topic_id=$id";delete.Parameters.AddWithValue("$id",topicId);delete.ExecuteNonQuery();}
        foreach(var (sourceId,enabled) in selections){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO topic_sources(topic_id,source_id,enabled) VALUES($t,$s,$e)";cmd.Parameters.AddWithValue("$t",topicId);cmd.Parameters.AddWithValue("$s",sourceId);cmd.Parameters.AddWithValue("$e",enabled?1:0);cmd.ExecuteNonQuery();}tx.Commit();
    }
    public bool IsSourceEnabledForTopic(long topicId,long sourceId)
    {
        using var c=Open();if(Scalar<long>(c,"SELECT COUNT(*) FROM topic_sources WHERE topic_id=$t",("$t",topicId))==0)return true;
        return Scalar<long>(c,"SELECT COUNT(*) FROM topic_sources WHERE topic_id=$t AND source_id=$s AND enabled=1",("$t",topicId),("$s",sourceId))>0;
    }
    public void SetSetting<T>(string key,T value){using var c=Open();Execute(c,"INSERT INTO settings(key,value_json,updated_utc) VALUES($k,$v,$t) ON CONFLICT(key) DO UPDATE SET value_json=$v,updated_utc=$t",("$k",key),("$v",JsonSerializer.Serialize(value)),("$t",Now()));}
    public T? GetSetting<T>(string key,T? fallback=default){using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT value_json FROM settings WHERE key=$k";cmd.Parameters.AddWithValue("$k",key);var value=cmd.ExecuteScalar() as string;return value is null?fallback:JsonSerializer.Deserialize<T>(value);}
    public void CreateRun(string id,string trigger,long? scheduleId=null){using var c=Open();Execute(c,"INSERT INTO runs(id,schedule_id,trigger_type,status,phase,started_utc,heartbeat_utc,worker_pid) VALUES($id,$sid,$trigger,'active','QUEUED',$now,$now,$pid)",("$id",id),("$sid",scheduleId),("$trigger",trigger),("$now",Now()),("$pid",Environment.ProcessId));}
    public void Phase(string id,string phase){using var c=Open();Execute(c,"UPDATE runs SET phase=$p,heartbeat_utc=$t WHERE id=$id",("$p",phase),("$t",Now()),("$id",id));}
    public void Heartbeat(string id){using var c=Open();Execute(c,"UPDATE runs SET heartbeat_utc=$t WHERE id=$id",("$t",Now()),("$id",id));}
    public void Finish(string id,string status,string? deferred=null,string? error=null){using var c=Open();Execute(c,"UPDATE runs SET status=$s,phase=$s,finished_utc=$t,deferred_reason=$d,error_message=$e WHERE id=$id",("$s",status),("$t",Now()),("$d",deferred),("$e",error),("$id",id));}
    public void Increment(string id,string field){if(field is not ("discovered_count" or "fetched_count" or "analyzed_count" or "duplicate_count" or "failed_count"))throw new ArgumentException(nameof(field));using var c=Open();Execute(c,$"UPDATE runs SET {field}={field}+1 WHERE id=$id",("$id",id));}
    public void Event(string id,string severity,string phase,string code,string message){using var c=Open();Execute(c,"INSERT INTO run_events(run_id,event_utc,severity,phase,event_code,message) VALUES($id,$t,$s,$p,$c,$m)",("$id",id),("$t",Now()),("$s",severity),("$p",phase),("$c",code),("$m",message));}
    public RunSummary? LatestRun(){using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT id,status,phase,discovered_count,fetched_count,analyzed_count,duplicate_count,failed_count,deferred_reason,started_utc,finished_utc FROM runs ORDER BY started_utc DESC LIMIT 1";using var r=cmd.ExecuteReader();return r.Read()?ReadRun(r):null;}
    public RunSummary? GetRun(string id){using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT id,status,phase,discovered_count,fetched_count,analyzed_count,duplicate_count,failed_count,deferred_reason,started_utc,finished_utc FROM runs WHERE id=$id";cmd.Parameters.AddWithValue("$id",id);using var r=cmd.ExecuteReader();return r.Read()?ReadRun(r):null;}
    public string? ActiveRunForPid(int pid){using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT id FROM runs WHERE worker_pid=$pid AND status='active' ORDER BY started_utc DESC LIMIT 1";cmd.Parameters.AddWithValue("$pid",pid);return cmd.ExecuteScalar() as string;}
    private static RunSummary ReadRun(SqliteDataReader r)=>new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetInt32(4),r.GetInt32(5),r.GetInt32(6),r.GetInt32(7),r.IsDBNull(8)?null:r.GetString(8),DateTimeOffset.Parse(r.GetString(9)),r.IsDBNull(10)?null:DateTimeOffset.Parse(r.GetString(10)));
    public void RecoverAbandoned(){using var c=Open();Execute(c,"UPDATE runs SET status='PARTIAL',phase='PARTIAL',finished_utc=$t,error_code='ABANDONED',error_message='Worker stopped before finishing' WHERE status='active' AND heartbeat_utc < $cutoff",("$t",Now()),("$cutoff",DateTimeOffset.UtcNow.AddSeconds(-120).ToString("O")));Execute(c,"UPDATE work_queue SET status='pending',leased_until_utc=NULL WHERE status='leased' AND leased_until_utc<$t",("$t",Now()));}
    public long AddDiscovery(string runId,Discovery d)
    {
        using var c=Open();Execute(c,"INSERT INTO discoveries(run_id,source_id,topic_id,discovered_url,canonical_url,title,snippet,published_utc,discovered_utc,deterministic_score,fetch_allowed,fetch_status) VALUES($r,$s,$t,$u,$c,$title,$snip,$pub,$date,$score,$allow,'pending')",("$r",runId),("$s",d.SourceId),("$t",d.TopicId),("$u",d.Url),("$c",d.CanonicalUrl),("$title",d.Title),("$snip",d.Snippet),("$pub",d.PublishedUtc?.ToString("O")),("$date",d.DiscoveredUtc.ToString("O")),("$score",d.Score),("$allow",d.FetchAllowed?1:0));return Scalar<long>(c,"SELECT last_insert_rowid()");
    }
    public void SetDiscoveryStatus(long id,string status){using var c=Open();Execute(c,"UPDATE discoveries SET fetch_status=$s WHERE id=$id",("$s",status),("$id",id));}
    public void SetDiscoveryDocument(long discoveryId,long documentId){using var c=Open();Execute(c,"UPDATE discoveries SET document_id=$d WHERE id=$id",("$d",documentId),("$id",discoveryId));}
    public (long Id,bool IsNew) UpsertDocument(Discovery discovery,string title,string text)
    {
        string normalized=ContentTools.Normalize(text);string hash=ContentTools.Hash(normalized);
        string file=System.IO.Path.Combine(DocumentRoot,hash+".md");Directory.CreateDirectory(DocumentRoot);
        if(!File.Exists(file))File.WriteAllText(file,normalized);
        using var c=Open();
        long existing=Scalar<long>(c,"SELECT COALESCE((SELECT id FROM documents WHERE canonical_url=$url OR content_hash=$hash ORDER BY id LIMIT 1),0)",("$url",discovery.CanonicalUrl),("$hash",hash));
        if(existing>0){
            string oldHash=Scalar<string>(c,"SELECT content_hash FROM documents WHERE id=$id",("$id",existing));
            if(oldHash!=hash && Scalar<long>(c,"SELECT COUNT(*) FROM documents WHERE content_hash=$hash",("$hash",hash))==0){
                Execute(c,"UPDATE documents SET title=$title,source_id=$source,published_utc=COALESCE($pub,published_utc),last_seen_utc=$t,content_hash=$hash,text_file_path=$file,character_count=$count WHERE id=$id",("$title",title),("$source",discovery.SourceId),("$pub",discovery.PublishedUtc?.ToString("O")),("$t",Now()),("$hash",hash),("$file",file),("$count",normalized.Length),("$id",existing));
                return(existing,true);
            }
            Execute(c,"UPDATE documents SET last_seen_utc=$t WHERE id=$id",("$t",Now()),("$id",existing));return(existing,false);
        }
        using(var recent=c.CreateCommand())
        {
            recent.CommandText="SELECT id,text_file_path FROM documents WHERE first_seen_utc>$cutoff AND text_file_path IS NOT NULL ORDER BY last_seen_utc DESC LIMIT 50";
            recent.Parameters.AddWithValue("$cutoff",DateTimeOffset.UtcNow.AddDays(-30).ToString("O"));
            using var rows=recent.ExecuteReader();
            while(rows.Read())
            {
                string candidateFile=rows.GetString(1);
                if(File.Exists(candidateFile) && ContentTools.Similarity(normalized,File.ReadAllText(candidateFile))>=.92)
                {
                    long duplicate=rows.GetInt64(0);rows.Close();Execute(c,"UPDATE documents SET last_seen_utc=$t WHERE id=$id",("$t",Now()),("$id",duplicate));return(duplicate,false);
                }
            }
        }
        Execute(c,"INSERT INTO documents(canonical_url,title,source_id,published_utc,first_seen_utc,last_seen_utc,content_hash,text_file_path,character_count) VALUES($u,$title,$s,$pub,$t,$t,$hash,$file,$count)",("$u",discovery.CanonicalUrl),("$title",title),("$s",discovery.SourceId),("$pub",discovery.PublishedUtc?.ToString("O")),("$t",Now()),("$hash",hash),("$file",file),("$count",normalized.Length));
        return(Scalar<long>(c,"SELECT last_insert_rowid()"),true);
    }
    public void LinkDocument(long documentId,long topicId,double score){using var c=Open();Execute(c,"INSERT INTO document_topics(document_id,topic_id,deterministic_score) VALUES($d,$t,$s) ON CONFLICT(document_id,topic_id) DO UPDATE SET deterministic_score=MAX(deterministic_score,$s)",("$d",documentId),("$t",topicId),("$s",score));}
    public string ImportChosenContent(long topicId,string url,string title,string text,string? existingRunId=null,bool requireLinkedIn=true)
    {
        if(text.Trim().Length<40)throw new ArgumentException("At least 40 characters of selected content are required.",nameof(text));
        var uri=new Uri(url);if(uri.Scheme is not("http" or "https") || requireLinkedIn && !SourcePolicy.IsLinkedIn(uri))throw new ArgumentException("A permitted source URL is required.",nameof(url));
        var source=Sources().First(x=>x.Type==(requireLinkedIn?"linkedin_capture":"manual"));
        if(!SourcePolicy.MayManualImport(source,true))throw new InvalidOperationException("Manual import policy denied");
        if(!Topics().Any(x=>x.Id==topicId))throw new ArgumentException("Unknown topic",nameof(topicId));
        string runId=existingRunId??Guid.NewGuid().ToString("N");if(existingRunId is null)CreateRun(runId,"user_capture");
        var d=new Discovery(source.Id,topicId,url,ContentTools.Canonicalize(url),title,ContentTools.Normalize(text[..Math.Min(text.Length,500)]),null,DateTimeOffset.UtcNow,false,1);
        long discoveryId=AddDiscovery(runId,d);Increment(runId,"discovered_count");
        var (docId,isNew)=UpsertDocument(d,title,text);SetDiscoveryDocument(discoveryId,docId);LinkDocument(docId,topicId,1);SetDiscoveryStatus(discoveryId,"user_captured");Increment(runId,"fetched_count");
        if(isNew)Queue(runId,docId);else Increment(runId,"duplicate_count");
        return runId;
    }
    public void Queue(string runId,long documentId){using var c=Open();Execute(c,"INSERT INTO work_queue(run_id,document_id,stage,status,created_utc,updated_utc) VALUES($r,$d,'analysis','pending',$t,$t)",("$r",runId),("$d",documentId),("$t",Now()));}
    public void RecoverPendingToRun(string newRunId)
    {
        using var c=Open();using var tx=c.BeginTransaction();
        var rows=new List<(long QueueId,long DocumentId,long SourceId,long TopicId,string? Url,string? Canonical,string? Title,string? Snippet,string? Published)>();
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;cmd.CommandText="SELECT q.id,q.document_id,x.source_id,x.topic_id,x.discovered_url,x.canonical_url,x.title,x.snippet,x.published_utc FROM work_queue q JOIN runs r ON r.id=q.run_id JOIN discoveries x ON x.run_id=q.run_id AND x.document_id=q.document_id WHERE q.status='pending' AND r.status IN ('PARTIAL','FAILED') AND NOT EXISTS(SELECT 1 FROM analyses a WHERE a.document_id=q.document_id) GROUP BY q.id LIMIT 100";
            using var reader=cmd.ExecuteReader();while(reader.Read())rows.Add((reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),reader.GetInt64(3),reader.IsDBNull(4)?null:reader.GetString(4),reader.IsDBNull(5)?null:reader.GetString(5),reader.IsDBNull(6)?null:reader.GetString(6),reader.IsDBNull(7)?null:reader.GetString(7),reader.IsDBNull(8)?null:reader.GetString(8)));
        }
        foreach(var row in rows)
        {
            using(var insert=c.CreateCommand())
            {
                insert.Transaction=tx;insert.CommandText="INSERT INTO discoveries(run_id,source_id,topic_id,discovered_url,canonical_url,title,snippet,published_utc,discovered_utc,fetch_allowed,fetch_status,document_id) VALUES($r,$s,$t,$u,$c,$title,$snippet,$pub,$now,0,'recovered',$doc)";
                insert.Parameters.AddWithValue("$r",newRunId);insert.Parameters.AddWithValue("$s",row.SourceId);insert.Parameters.AddWithValue("$t",row.TopicId);insert.Parameters.AddWithValue("$u",(object?)row.Url??DBNull.Value);insert.Parameters.AddWithValue("$c",(object?)row.Canonical??DBNull.Value);insert.Parameters.AddWithValue("$title",(object?)row.Title??DBNull.Value);insert.Parameters.AddWithValue("$snippet",(object?)row.Snippet??DBNull.Value);insert.Parameters.AddWithValue("$pub",(object?)row.Published??DBNull.Value);insert.Parameters.AddWithValue("$now",Now());insert.Parameters.AddWithValue("$doc",row.DocumentId);insert.ExecuteNonQuery();
            }
            using(var queue=c.CreateCommand()){queue.Transaction=tx;queue.CommandText="INSERT INTO work_queue(run_id,document_id,stage,status,created_utc,updated_utc) VALUES($r,$d,'analysis','pending',$t,$t)";queue.Parameters.AddWithValue("$r",newRunId);queue.Parameters.AddWithValue("$d",row.DocumentId);queue.Parameters.AddWithValue("$t",Now());queue.ExecuteNonQuery();}
            using(var update=c.CreateCommand()){update.Transaction=tx;update.CommandText="UPDATE work_queue SET status='superseded',updated_utc=$t WHERE id=$id";update.Parameters.AddWithValue("$t",Now());update.Parameters.AddWithValue("$id",row.QueueId);update.ExecuteNonQuery();}
        }
        tx.Commit();
        if(rows.Count>0)Execute(c,"UPDATE runs SET discovered_count=discovered_count+$n WHERE id=$r",("$n",rows.Count),("$r",newRunId));
    }
    public IReadOnlyList<(long QueueId,Document Document)> Pending(string runId)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT q.id,d.id,d.source_id,d.canonical_url,d.title,d.text_file_path,d.content_hash,d.published_utc,d.first_seen_utc,q.document_id,dt.topic_id,dt.deterministic_score FROM work_queue q JOIN documents d ON d.id=q.document_id JOIN document_topics dt ON dt.document_id=d.id WHERE q.run_id=$r AND q.status='pending' ORDER BY q.id";cmd.Parameters.AddWithValue("$r",runId);
        using var r=cmd.ExecuteReader();var list=new List<(long,Document)>();while(r.Read()){string file=r.IsDBNull(5)?"":r.GetString(5);string text=File.Exists(file)?File.ReadAllText(file):"";list.Add((r.GetInt64(0),new Document(r.GetInt64(1),r.GetInt64(2),r.IsDBNull(3)?null:r.GetString(3),r.GetString(4),text,r.GetString(6),r.IsDBNull(7)?null:DateTimeOffset.Parse(r.GetString(7)),DateTimeOffset.Parse(r.GetString(8)),true,r.GetInt64(10),r.GetDouble(11))));}return list;
    }
    public void CompleteQueue(long id,string status,string? error=null){using var c=Open();Execute(c,"UPDATE work_queue SET status=$s,attempt_count=attempt_count+1,last_error=$e,updated_utc=$t WHERE id=$id",("$s",status),("$e",error),("$t",Now()),("$id",id));}
    public void SaveAnalysis(Analysis a){using var c=Open();Execute(c,"INSERT INTO analyses(document_id,model_key,prompt_version,analyzed_utc,relevance_score,novelty_score,summary,relevance_reason,raw_result_json) VALUES($d,$m,'v1',$t,$r,$n,$s,$reason,$raw)",("$d",a.DocumentId),("$m",a.ModelKey),("$t",Now()),("$r",a.RelevanceScore),("$n",a.NoveltyScore),("$s",a.Summary),("$reason",a.RelevanceReason),("$raw",a.RawJson));}
    public IReadOnlyList<ReportItem> ReportItems(string runId,bool loadText=false)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT d.id,d.source_id,x.canonical_url,d.title,x.snippet,d.content_hash,d.published_utc,d.first_seen_utc,t.id,t.name,t.description,t.priority,t.max_age_hours,t.max_results_per_run,t.enabled,s.name,s.source_type,s.base_uri,s.policy_mode,s.enabled,s.config_json,a.model_key,a.relevance_score,a.novelty_score,a.summary,a.relevance_reason,a.raw_result_json,x.discovered_utc,a.analyzed_utc,d.text_file_path FROM discoveries x JOIN documents d ON d.id=x.document_id JOIN topics t ON t.id=x.topic_id JOIN sources s ON s.id=x.source_id LEFT JOIN analyses a ON a.id=(SELECT MAX(id) FROM analyses WHERE document_id=d.id) WHERE x.run_id=$r GROUP BY d.id,t.id ORDER BY COALESCE(a.relevance_score,x.deterministic_score) DESC";cmd.Parameters.AddWithValue("$r",runId);using var r=cmd.ExecuteReader();var list=new List<ReportItem>();var runStarted=GetRun(runId)?.StartedUtc??DateTimeOffset.MinValue;while(r.Read()){
            var firstSeen=DateTimeOffset.Parse(r.GetString(7));
            bool newInRun=firstSeen>=runStarted || !r.IsDBNull(28) && DateTimeOffset.Parse(r.GetString(28))>=runStarted;
            string text=r.IsDBNull(4)?"":r.GetString(4); if(loadText && !r.IsDBNull(29) && File.Exists(r.GetString(29)))text=File.ReadAllText(r.GetString(29));
            var doc=new Document(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetString(2),r.GetString(3),text,r.GetString(5),r.IsDBNull(6)?null:DateTimeOffset.Parse(r.GetString(6)),firstSeen,newInRun,r.GetInt64(8),0);
            var topic=new Topic(r.GetInt64(8),r.GetString(9),r.GetString(10),r.GetInt32(11),r.GetInt32(12),r.GetInt32(13),r.GetInt32(14)!=0,[]);
            var source=new Source(r.GetInt64(1),r.GetString(15),r.GetString(16),r.IsDBNull(17)?null:r.GetString(17),r.GetString(18),r.GetInt32(19)!=0,r.IsDBNull(20)?null:r.GetString(20));
            Analysis? a=r.IsDBNull(21)?null:new(doc.Id,r.GetString(21),r.GetDouble(22),r.IsDBNull(23)?0:r.GetDouble(23),r.GetString(24),r.IsDBNull(25)?"":r.GetString(25),r.GetString(26));list.Add(new(doc,topic,source,a,DateTimeOffset.Parse(r.GetString(27))));
        }return list;
    }
    public void SaveReport(string runId,string path,string? summary,bool partial,ReportArtifacts? artifacts=null)
    {
        using var c=Open();Execute(c,"INSERT INTO reports(run_id,report_date,title,summary,html_path,generated_utc,partial) VALUES($r,$date,$title,$s,$path,$t,$partial)",("$r",runId),("$date",DateTimeOffset.Now.ToString("yyyy-MM-dd")),("$title","Signal Atlas"),("$s",summary),("$path",path),("$t",Now()),("$partial",partial?1:0));
        long reportId=Scalar<long>(c,"SELECT last_insert_rowid()");
        int order=0;foreach(var item in ReportItems(runId))Execute(c,"INSERT OR IGNORE INTO report_items(report_id,document_id,topic_id,display_order) VALUES($r,$d,$t,$o)",("$r",reportId),("$d",item.Document.Id),("$t",item.Topic.Id),("$o",order++));
        if(artifacts is not null)Execute(c,"UPDATE reports SET cards_path=$c,report_path=$r,pdf_path=$p,report_data_path=$d WHERE id=$id",("$c",artifacts.CardsHtml),("$r",artifacts.ReportHtml),("$p",artifacts.Pdf),("$d",artifacts.DataJson),("$id",reportId));
    }
    public ReportArtifacts ReportFiles(string path)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT cards_path,report_path,pdf_path,report_data_path FROM reports WHERE html_path=$p ORDER BY id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$p",path);using var r=cmd.ExecuteReader();
        return r.Read()?new(path,r.IsDBNull(0)?null:r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.IsDBNull(3)?null:r.GetString(3)):new(path,null,null,null,null);
    }
    public IReadOnlyList<(string Path,string Date)> Reports(){using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT html_path,report_date FROM reports ORDER BY generated_utc DESC";using var r=cmd.ExecuteReader();var list=new List<(string,string)>();while(r.Read())list.Add((r.GetString(0),r.GetString(1)));return list;}
    public string? ReportSummary(string path){using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT summary FROM reports WHERE html_path=$p ORDER BY id DESC LIMIT 1";cmd.Parameters.AddWithValue("$p",path);return cmd.ExecuteScalar() as string;}
    public IReadOnlyList<BookmarkEntry> ReportEntries(string path)
    {
        using var c=Open();using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT i.document_id,i.topic_id,d.title,t.name,i.bookmarked FROM report_items i JOIN reports r ON r.id=i.report_id JOIN documents d ON d.id=i.document_id JOIN topics t ON t.id=i.topic_id WHERE r.html_path=$p ORDER BY i.display_order";
        cmd.Parameters.AddWithValue("$p",path);using var rows=cmd.ExecuteReader();var list=new List<BookmarkEntry>();
        while(rows.Read())list.Add(new(rows.GetInt64(0),rows.GetInt64(1),rows.GetString(2),rows.GetString(3),rows.GetInt32(4)!=0));
        return list;
    }
    public void SetBookmark(string reportPath,long documentId,long topicId,bool bookmarked)
    {
        using var c=Open();Execute(c,"UPDATE report_items SET bookmarked=$b WHERE document_id=$d AND topic_id=$t AND report_id IN (SELECT id FROM reports WHERE html_path=$p)",("$b",bookmarked?1:0),("$d",documentId),("$t",topicId),("$p",reportPath));
    }
    public void DeleteReport(string path)
    {
        string full=System.IO.Path.GetFullPath(path),root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppPaths.Reports,"Reports")).TrimEnd(System.IO.Path.DirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar;
        if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Report path is outside the report archive");
        var files=ReportFiles(path);using var c=Open();Execute(c,"DELETE FROM reports WHERE html_path=$p",("$p",path));
        foreach(string file in files.Paths){string resolved=System.IO.Path.GetFullPath(file);if(resolved.StartsWith(root,StringComparison.OrdinalIgnoreCase) && File.Exists(resolved))File.Delete(resolved);}
        var newest=Reports().FirstOrDefault();string latest=System.IO.Path.Combine(AppPaths.Reports,"Latest Report.html");
        if(newest.Path is not null && File.Exists(newest.Path))
        {
            string html=File.ReadAllText(newest.Path);foreach(string file in ReportFiles(newest.Path).Paths)html=html.Replace("href=\""+System.IO.Path.GetFileName(file)+"\"","href=\"Reports/"+System.IO.Path.GetFileName(file)+"\"");File.WriteAllText(latest,html);
        }
        else if(File.Exists(latest))File.Delete(latest);
    }
    public long SaveSchedule(MonitorSchedule s)
    {
        if(!TimeOnly.TryParse(s.TimeOfDay,out _))throw new ArgumentException("Invalid schedule time");
        if(s.ReportMode is not("normal" or "discovery_only"))throw new ArgumentException("Invalid schedule report mode");
        using var c=Open();string json=JsonSerializer.Serialize(new{time=s.TimeOfDay,days=s.Days,topics=s.TopicIds??[],reportMode=s.ReportMode});
        if(s.Id==0){Execute(c,"INSERT INTO schedules(name,enabled,schedule_type,schedule_json,start_when_available,wake_to_run,max_runtime_minutes,created_utc,updated_utc) VALUES($n,$e,'weekly',$j,$start,$wake,$max,$t,$t)",("$n",s.Name),("$e",s.Enabled?1:0),("$j",json),("$start",s.StartWhenAvailable?1:0),("$wake",s.WakeToRun?1:0),("$max",s.MaxRuntimeMinutes),("$t",Now()));return Scalar<long>(c,"SELECT last_insert_rowid()");}
        Execute(c,"UPDATE schedules SET name=$n,enabled=$e,schedule_json=$j,start_when_available=$start,wake_to_run=$wake,max_runtime_minutes=$max,updated_utc=$t WHERE id=$id",("$n",s.Name),("$e",s.Enabled?1:0),("$j",json),("$start",s.StartWhenAvailable?1:0),("$wake",s.WakeToRun?1:0),("$max",s.MaxRuntimeMinutes),("$t",Now()),("$id",s.Id));return s.Id;
    }
    public IReadOnlyList<MonitorSchedule> Schedules()
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT id,name,enabled,schedule_json,start_when_available,wake_to_run,max_runtime_minutes FROM schedules ORDER BY id";using var r=cmd.ExecuteReader();var list=new List<MonitorSchedule>();while(r.Read()){
            using var json=JsonDocument.Parse(r.GetString(3));var root=json.RootElement;list.Add(new(r.GetInt64(0),r.GetString(1),r.GetInt32(2)!=0,root.GetProperty("time").GetString()??"08:00",root.GetProperty("days").EnumerateArray().Select(x=>x.GetString()??"").ToArray(),r.GetInt32(4)!=0,r.GetInt32(5)!=0,r.GetInt32(6),root.TryGetProperty("topics",out var topicIds)?topicIds.EnumerateArray().Select(x=>x.GetInt64()).ToArray():[],root.TryGetProperty("reportMode",out var reportMode)?reportMode.GetString()??"normal":"normal"));
        }return list;
    }
    public void DeleteSchedule(long id){using var c=Open();using var tx=c.BeginTransaction();using(var cmd=c.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="UPDATE runs SET schedule_id=NULL WHERE schedule_id=$id; DELETE FROM schedules WHERE id=$id";cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();}tx.Commit();}
}
