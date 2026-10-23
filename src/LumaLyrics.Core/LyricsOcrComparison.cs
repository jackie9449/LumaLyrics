using System.Text;
namespace LumaLyrics.Core;

public sealed record LyricsCaptureBounds(int Left,int Top,int Width,int Height);
public sealed record LyricsOcrObservation(Track Track,string SourceHash,DateTimeOffset CapturedUtc,double? PositionMs,string Language,LyricsCaptureBounds Bounds,IReadOnlyList<string> Lines);
public enum LyricsOcrMatch { Matched,Similar,Ambiguous,Unmatched }
public sealed record LyricsOcrComparedLine(string Recognized,int? GroupIndex,string? Original,LyricsOcrMatch Match,double Similarity);
public sealed record LyricsOcrReport(bool Accepted,string Message,IReadOnlyList<LyricsOcrComparedLine> Lines,int ObservedGroups,int TotalGroups)
{
    // Even complete text coverage is not a timing or semantic review.
    public bool FullSongReviewed=>false;
}
public static class LyricsOcrComparison
{
    sealed record OriginalLine(int Group,string Text,string Key);
    public static string Normalize(string value)=>string.Concat(value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().EnumerateRunes().Where(Rune.IsLetterOrDigit));
    public static LyricsOcrReport Compare(Track expected,LyricsDocument document,LyricsOcrObservation observation)
    {
        var originals=document.Groups.SelectMany((g,i)=>g.Lines.Select(line=>new OriginalLine(i,line,Normalize(line)))).Where(x=>TranslationAlignment.IsLyricText(x.Text)).Take(3000).ToArray();
        int total=originals.Select(x=>x.Group).Distinct().Count();
        if(expected.Fingerprint!=observation.Track.Fingerprint || expected.DurationMs is { } a && observation.Track.DurationMs is { } b && Math.Abs(a-b)>1000 || TranslationAlignment.Hash(document.Raw)!=observation.SourceHash)
            return new(false,"当前录音或原词已变化，这次识别未用于校对。",[],0,total);
        if(document.Instrumental || !document.IsSynced)return new(false,"需要可确认的动态原词才能比较；识别不会生成时间戳。",[],0,total);
        var seen=observation.Lines.Where(line=>!string.IsNullOrWhiteSpace(line)).Take(120).Select(line=>line[..Math.Min(line.Length,1000)]).ToArray();
        int m=seen.Length,n=originals.Length;var keys=seen.Select(Normalize).ToArray();var scores=new double[m+1,n+1];var steps=new byte[m+1,n+1];
        for(int i=1;i<=m;i++)scores[i,0]=-.7*i;
        for(int i=1;i<=m;i++)for(int j=1;j<=n;j++)
        {
            double best=scores[i-1,j]-.7;byte step=1;
            if(scores[i,j-1]-.12>best){best=scores[i,j-1]-.12;step=0;}
            double similarity=Similarity(keys[i-1],originals[j-1].Key);
            if(similarity>=.72 && scores[i-1,j-1]+2*similarity>best){best=scores[i-1,j-1]+2*similarity;step=2;}
            if(i>=2 && keys[i-2].Length>0 && keys[i-1].Length>0)
            {
                double joined=Similarity(keys[i-2]+keys[i-1],originals[j-1].Key);
                if(joined>=.85 && scores[i-2,j-1]+2.2*joined>best){best=scores[i-2,j-1]+2.2*joined;step=3;}
            }
            scores[i,j]=best;steps[i,j]=step;
        }
        int end=Enumerable.Range(0,n+1).MaxBy(j=>scores[m,j]);var lines=new List<LyricsOcrComparedLine>();int row=m,column=end;
        while(row>0)
        {
            byte step=column==0?(byte)1:steps[row,column];
            if(step==0){column--;continue;}
            if(step==1){lines.Add(new(seen[--row],null,null,LyricsOcrMatch.Unmatched,0));continue;}
            var original=originals[column-1];int count=step==3?2:1;string text=string.Join(" ",seen.Skip(row-count).Take(count));
            double similarity=Similarity(string.Concat(keys.Skip(row-count).Take(count)),original.Key);
            var kind=similarity<1?LyricsOcrMatch.Similar:originals.Count(x=>x.Key==original.Key)>1?LyricsOcrMatch.Ambiguous:LyricsOcrMatch.Matched;
            lines.Add(new(text,original.Group,original.Text,kind,similarity));row-=count;column--;
        }
        lines.Reverse();int observed=lines.Where(x=>x.Match==LyricsOcrMatch.Matched).Select(x=>x.GroupIndex).Distinct().Count();
        return new(true,$"本次可见文字对应 {observed}/{total} 个原词组；相近、重复和未匹配文字需查看。",lines,observed,total);
    }
    static double Similarity(string a,string b)
    {
        if(a.Length==0 || b.Length==0)return 0;if(a==b)return 1;
        int length=Math.Max(a.Length,b.Length);if(length<5 || Math.Min(a.Length,b.Length)<length*.65 || length>1000)return 0;
        var previous=Enumerable.Range(0,b.Length+1).ToArray();var next=new int[b.Length+1];
        for(int i=1;i<=a.Length;i++)
        {
            next[0]=i;for(int j=1;j<=b.Length;j++)next[j]=Math.Min(Math.Min(previous[j]+1,next[j-1]+1),previous[j-1]+(a[i-1]==b[j-1]?0:1));
            (previous,next)=(next,previous);
        }
        return 1-previous[b.Length]/(double)length;
    }
}
