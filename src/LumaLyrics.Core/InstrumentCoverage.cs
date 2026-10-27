namespace LumaLyrics.Core;

/// <summary>Union of actually observed audio intervals, without filling internal gaps.</summary>
public sealed class InstrumentCoverage
{
    readonly List<InstrumentSpan> ranges = [];
    public IReadOnlyList<InstrumentSpan> Ranges => ranges.ToArray();

    public void Add(double startMs, double endMs)
    {
        if (!double.IsFinite(startMs) || startMs < 0) throw new ArgumentOutOfRangeException(nameof(startMs));
        if (!double.IsFinite(endMs) || endMs <= startMs) throw new ArgumentOutOfRangeException(nameof(endMs));
        int first = 0;
        while (first < ranges.Count && ranges[first].EndMs < startMs) first++;
        int last = first;
        while (last < ranges.Count && ranges[last].StartMs <= endMs)
        {
            startMs = Math.Min(startMs, ranges[last].StartMs);
            endMs = Math.Max(endMs, ranges[last].EndMs);
            last++;
        }
        if (last > first) ranges.RemoveRange(first, last - first);
        ranges.Insert(first, new(startMs, endMs));
    }

    public bool IsComplete(double durationMs, double toleranceMs = 50)
    {
        if (!double.IsFinite(durationMs) || durationMs <= 0) throw new ArgumentOutOfRangeException(nameof(durationMs));
        if (!double.IsFinite(toleranceMs) || toleranceMs < 0 || toleranceMs >= durationMs)
            throw new ArgumentOutOfRangeException(nameof(toleranceMs));
        return ranges.Count == 1 && ranges[0].StartMs <= toleranceMs && ranges[0].EndMs >= durationMs - toleranceMs;
    }
}
