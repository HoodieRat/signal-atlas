using SignalAtlas.Core;

namespace SignalAtlas.Data;

public sealed class RetentionService(ResearchDatabase database)
{
    public void Apply(int reportsDays=180,int contentDays=30,int cacheDays=7,int logsDays=30)
    {
        using var c=database.Open();
        string reportCutoff=DateTimeOffset.UtcNow.AddDays(-reportsDays).ToString("O");
        var oldReports=new List<(long Id,string Path)>();
        using(var cmd=c.CreateCommand())
        {
            cmd.CommandText="SELECT r.id,r.html_path FROM reports r WHERE r.generated_utc<$cutoff AND NOT EXISTS(SELECT 1 FROM report_items i WHERE i.report_id=r.id AND i.bookmarked=1)";
            cmd.Parameters.AddWithValue("$cutoff",reportCutoff);using var rows=cmd.ExecuteReader();while(rows.Read())oldReports.Add((rows.GetInt64(0),rows.GetString(1)));
        }
        foreach(var (id,path) in oldReports){foreach(string file in database.ReportFiles(path).Paths)DeleteWithin(file,Path.Combine(AppPaths.Reports,"Reports"));ResearchDatabase.Execute(c,"DELETE FROM reports WHERE id=$id",("$id",id));}
        string contentCutoff=DateTimeOffset.UtcNow.AddDays(-contentDays).ToString("O");
        var oldDocuments=new List<(long Id,string Path)>();
        using(var cmd=c.CreateCommand())
        {
            cmd.CommandText="SELECT d.id,d.text_file_path FROM documents d WHERE d.last_seen_utc<$cutoff AND d.text_file_path IS NOT NULL AND NOT EXISTS(SELECT 1 FROM report_items i WHERE i.document_id=d.id AND i.bookmarked=1)";
            cmd.Parameters.AddWithValue("$cutoff",contentCutoff);using var rows=cmd.ExecuteReader();while(rows.Read())oldDocuments.Add((rows.GetInt64(0),rows.GetString(1)));
        }
        foreach(var (id,path) in oldDocuments){DeleteWithin(path,AppPaths.Documents);ResearchDatabase.Execute(c,"UPDATE documents SET text_file_path=NULL WHERE id=$id",("$id",id));}
        DeleteOldFiles(Path.Combine(AppPaths.Root,"cache","searches"),cacheDays);
        DeleteOldFiles(AppPaths.Logs,logsDays);
        DeleteOldFiles(Path.Combine(AppPaths.Root,"cache","temporary"),0);
    }
    private static void DeleteOldFiles(string root,int days)
    {
        if(!Directory.Exists(root))return;
        foreach(var path in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories))
            if(File.GetLastWriteTimeUtc(path)<DateTime.UtcNow.AddDays(-days))DeleteWithin(path,root);
    }
    private static void DeleteWithin(string path,string root)
    {
        string full=Path.GetFullPath(path),prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) && File.Exists(full))File.Delete(full);
    }
}
