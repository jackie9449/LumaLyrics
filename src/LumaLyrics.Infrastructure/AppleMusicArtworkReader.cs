using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure;

public sealed record AppleArtworkSource(string PersistentId,string ArtworkId,Uri? RemoteUri,string? LocalPath,int SourceKind=0);

/// <summary>Reads only Apple's artwork mapping and image metadata, never account or playback databases.</summary>
public sealed partial class AppleMusicArtworkReader
{
    [GeneratedRegex(@"/(?<w>\d{1,5})x(?<h>\d{1,5})(?<suffix>bb[^/]*)$",RegexOptions.IgnoreCase)]
    private static partial Regex SizeSuffix();
    public static Uri? HighResolutionUri(string location,int locationType,int targetSize=1200)
    {
        targetSize=Math.Clamp(targetSize,1200,4000);
        if(locationType==4)
        {
            if(!location.StartsWith("Music",StringComparison.Ordinal) || location.Contains("..") || location.Contains('\\') || location.Contains('?') || location.Contains('#'))return null;
            location="https://is1-ssl.mzstatic.com/image/thumb/"+location+$"/{targetSize}x{targetSize}bb.jpg";
        }
        else if(locationType!=2)return null;
        location=location.Replace("{w}",targetSize.ToString()).Replace("{h}",targetSize.ToString());
        var match=SizeSuffix().Match(location);
        if(match.Success && (int.Parse(match.Groups["w"].Value)<targetSize || int.Parse(match.Groups["h"].Value)<targetSize))
        {
            int size=Math.Max(targetSize,Math.Max(int.Parse(match.Groups["w"].Value),int.Parse(match.Groups["h"].Value)));
            location=location[..match.Index]+$"/{size}x{size}"+match.Groups["suffix"].Value;
        }
        return Uri.TryCreate(location,UriKind.Absolute,out var uri) && IsAppleImage(uri)?uri:null;
    }
    public static bool IsAppleImage(Uri uri)=>uri.Scheme=="https" && uri.IsDefaultPort && uri.UserInfo.Length==0 && uri.Host.EndsWith(".mzstatic.com",StringComparison.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string,AppleArtworkSource> Read(string libraryPath)
    {
        var root=Path.GetDirectoryName(Path.GetFullPath(libraryPath))!;
        var file=Path.Combine(root,"artwork.sqlite");var result=new Dictionary<string,AppleArtworkSource>(StringComparer.OrdinalIgnoreCase);
        if(!File.Exists(file))return result;
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());db.Open();
        var localImages=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        using(var c=db.CreateCommand())
        {
            c.CommandText="SELECT artwork_id,size_kind,extension FROM cache_items WHERE status=1 ORDER BY width*height DESC";
            using var reader=c.ExecuteReader();
            while(reader.Read())
            {
                var artId=reader.GetString(0);var extension=reader.IsDBNull(2)?"":reader.GetString(2);
                if(localImages.ContainsKey(artId) || !Guid.TryParse(artId,out _) || extension is not ("jpg" or "jpeg" or "png"))continue;
                var path=Path.Combine(root,"artwork",$"{artId}_sk{reader.GetInt32(1)}.{extension}");
                if(File.Exists(path))localImages[artId]=path;
            }
        }
        using(var c=db.CreateCommand())
        {
            c.CommandText="SELECT i.item_id,i.artwork_id,s.location_type,s.location,i.source_kind FROM item_to_artwork i LEFT JOIN artwork_source s USING(artwork_id)";
            using var reader=c.ExecuteReader();
            while(reader.Read())
            {
                var id=unchecked((ulong)reader.GetInt64(0)).ToString("X16");var artworkId=reader.GetString(1);
                Uri? uri=reader.IsDBNull(2)||reader.IsDBNull(3)?null:HighResolutionUri(reader.GetString(3),reader.GetInt32(2));
                result[id]=new(id,artworkId,uri,localImages.GetValueOrDefault(artworkId),reader.GetInt32(4));
            }
        }
        return result;
    }
}
