using System.Text.Json;
namespace LumaLyrics.Infrastructure;

internal static class TranslationJobBackup
{
    static List<Dictionary<string,object?>> Rows(Microsoft.Data.Sqlite.SqliteConnection db,string table)
    {
        using var c=db.CreateCommand();c.CommandText="SELECT * FROM "+table;using var r=c.ExecuteReader();var rows=new List<Dictionary<string,object?>>();
        while(r.Read()){var row=new Dictionary<string,object?>();for(int i=0;i<r.FieldCount;i++)row[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i);rows.Add(row);}return rows;
    }
    internal static void Validate(string root,IEnumerable<PersonalLibraryFile> files)
    {
        var records=files.Where(f=>f.Kind=="translation-state").ToArray();if(records.Length==0)return;
        using var db=PersonalLibrarySnapshot.Open(PersonalLibraryArchive.AssetPath(root,"state/translations.db"));
        foreach(var file in records)
        {
            string table=Path.GetFileNameWithoutExtension(file.Path);
            if(table is not ("TranslationJobs" or "TranslationBatches" or "TranslationHistory")||file.Bytes>64*1024*1024)throw new InvalidDataException("不支持的译文任务快照。");
            using var actual=JsonDocument.Parse(File.ReadAllText(PersonalLibraryArchive.AssetPath(root,file.Path)));
            using var expected=JsonDocument.Parse(JsonSerializer.Serialize(Rows(db,table)));
            if(!JsonElement.DeepEquals(actual.RootElement,expected.RootElement))throw new InvalidDataException("可读译文历史与数据库快照不一致。");
        }
    }
    // Only the already-created SQLite snapshot is read, never live task files.
    internal static void Export(string state,string staging,PersonalLibraryManifest manifest)
    {
        using var db=PersonalLibrarySnapshot.Open(Path.Combine(state,"translations.db"));
        using(var v=db.CreateCommand()){v.CommandText="PRAGMA user_version";if(Convert.ToInt32(v.ExecuteScalar())<2)return;}
        foreach(string table in new[]{"TranslationJobs","TranslationBatches","TranslationHistory"})
        {
            var rows=Rows(db,table);
            string relative="translation-state/"+table+".json";string path=PersonalLibraryArchive.AssetPath(staging,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));PersonalLibraryArchive.AddFile(staging,relative,"translation-state",manifest);
        }
        manifest.Warnings.Add("本地翻译任务和批次记录来自一致数据库快照；agent未提交的临时文件及自行记录的进度不作为已接收成果。恢复后请重新生成入口，旧路径不可执行。");
    }
}
