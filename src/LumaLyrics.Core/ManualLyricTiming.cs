using System.Globalization;

namespace LumaLyrics.Core;

public sealed record LyricTimingLine(string Text,int? StartMs=null,int? EndMs=null);

/// <summary>Records only boundaries explicitly marked or edited by the listener.</summary>
public sealed class ManualLyricTiming
{
    const int MaximumMs=86400000;
    readonly LyricTimingLine[] lines;
    readonly Stack<Change> history=new();
    readonly int maximumMs;
    readonly record struct Change(int Index,LyricTimingLine Before,LyricTimingLine? Next,int Active);

    public ManualLyricTiming(string source,double? durationMs)
    {
        if(source.Length>128*1024)throw new InvalidDataException("打轴原词超过 128 KB。");
        var parsed=new LrcParser().Parse(source);
        if(parsed.IsSynced || source.Contains("[offset:",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请粘贴没有时间标签的静态原词。");
        var text=source.TrimStart('\uFEFF').Replace("\r\n","\n").Replace('\r','\n').Split('\n')
            .Select(line=>line.Trim()).Where(line=>line.Length>0).ToArray();
        if(text.Length is 0 or >1000)throw new InvalidDataException("请输入 1 到 1000 行静态原词。");
        lines=text.Select(line=>new LyricTimingLine(line)).ToArray();
        maximumMs=durationMs is >0 and <=MaximumMs?(int)Math.Floor(durationMs.Value):MaximumMs;
    }

    public IReadOnlyList<LyricTimingLine> Lines=>lines;
    public int ActiveIndex {get;private set;}
    public bool Complete=>ActiveIndex==lines.Length;
    public bool WaitingForStart=>!Complete && lines[ActiveIndex].StartMs is null;
    public bool CanJoinNext=>ActiveIndex+1<lines.Length && !WaitingForStart;
    public bool CanUndo=>history.Count>0;

    public void Mark(double positionMs,bool startNext=false)
    {
        if(Complete)throw new InvalidDataException("所有歌词已经标记，可以校对并保存。");
        int at=CheckedTime(positionMs);
        int index=ActiveIndex;
        var line=lines[index];
        if(line.StartMs is null)
        {
            if(startNext)throw new InvalidDataException("请先标记这一句开始唱的时间。");
            if(index>0 && at<lines[index-1].EndMs)throw new InvalidDataException("下一句不能早于上一句的结束时间。");
            history.Push(new(index,line,null,index));
            lines[index]=line with{StartMs=at};
            return;
        }
        if(at<=line.StartMs)throw new InvalidDataException("结束时间须晚于开始时间。可以撤销后重标。");
        bool join=startNext && CanJoinNext;
        history.Push(new(index,line,join?lines[index+1]:null,index));
        lines[index]=line with{EndMs=at};
        ActiveIndex++;
        if(join)lines[index+1]=lines[index+1] with{StartMs=at};
    }

    public void Edit(int index,double startMs,double endMs)
    {
        var line=lines[index];
        if(line.EndMs is null)throw new InvalidDataException("请先完成这一句的标记。");
        int start=CheckedTime(startMs),end=CheckedTime(endMs);
        if(end<=start || index>0 && start<lines[index-1].EndMs || index+1<lines.Length && end>lines[index+1].StartMs)
            throw new InvalidDataException("请保持每句起止时间正确，且相邻歌词不重叠。");
        if(line.StartMs==start && line.EndMs==end)return;
        history.Push(new(index,line,null,ActiveIndex));
        lines[index]=line with{StartMs=start,EndMs=end};
    }

    public void Undo()
    {
        if(!history.TryPop(out var change))return;
        lines[change.Index]=change.Before;
        if(change.Next is not null)lines[change.Index+1]=change.Next;
        ActiveIndex=change.Active;
    }

    public int LineAt(double positionMs)
    {
        for(int i=0;i<lines.Length;i++)if(lines[i].StartMs<=positionMs && lines[i].EndMs>positionMs)return i;
        return -1;
    }

    public string MakeLrc()
    {
        if(!Complete)throw new InvalidDataException("请标记所有歌词的起止时间后再保存。");
        var output=new List<string>(lines.Length*2);
        for(int i=0;i<lines.Length;i++)
        {
            var line=lines[i];
            output.Add("["+FormatTime(line.StartMs!.Value)+"]"+line.Text);
            if(i==lines.Length-1 || line.EndMs<lines[i+1].StartMs)
                output.Add("["+FormatTime(line.EndMs!.Value)+"]");
        }
        string lrc=string.Join('\n',output);
        var parsed=new LrcParser().Parse(lrc);
        if(!parsed.Groups.Where(g=>g.Text.Length>0).Select(g=>g.Text).SequenceEqual(lines.Select(l=>l.Text)))
            throw new InvalidDataException("原词包含 LRC 控制标签，请移除标签后重试。");
        return lrc;
    }

    int CheckedTime(double time)
    {
        if(!double.IsFinite(time) || time<0 || time>maximumMs)throw new InvalidDataException("时间超出歌曲范围。");
        return (int)Math.Floor(time);
    }

    public static string FormatTime(double positionMs)
    {
        int ms=double.IsFinite(positionMs)?(int)Math.Clamp(positionMs,0,MaximumMs):0;
        return string.Create(CultureInfo.InvariantCulture,$"{ms/60000:00}:{ms/1000%60:00}.{ms%1000:000}");
    }

    public static bool TryParseTime(string value,out double ms)
    {
        ms=0;
        var parts=value.Trim().Split(':');
        if(parts.Length is <1 or >2 || !decimal.TryParse(parts[^1],NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var seconds))return false;
        int minutes=0;
        if(parts.Length==2 && (!int.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out minutes) || seconds>=60))return false;
        if(seconds is <0 or >86400 || minutes>1440)return false;
        ms=(double)(minutes*60000m+seconds*1000m);
        return ms<=MaximumMs;
    }
}
