using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

internal static class InstrumentTimelineValidation
{
    internal static InstrumentTimeline Normalize(InstrumentTimeline value)
    {
        if (value is null || value.Recording is null) throw new InvalidDataException("Missing instrument timeline identity.");
        InstrumentRecordingIdentity.Validate(value.Recording);
        if (string.IsNullOrWhiteSpace(value.ProfileId) || value.ProfileId.Length > 512 || value.ProfileId.Any(char.IsControl)
            || value.Revision < 0 || value.Coverage is null || value.Scores is null || value.Intervals is null || value.Roster is null
            || value.Coverage.Length > 500000 || value.Scores.Length > 500000 || value.Intervals.Length > 500000)
            throw new InvalidDataException("Invalid instrument timeline metadata.");
        var coverage = new InstrumentCoverage();
        foreach (var span in value.Coverage)
        { if (span is null) throw new InvalidDataException("Missing instrument coverage."); CheckRange(span.StartMs, span.EndMs, value.Recording.DurationMs); coverage.Add(span.StartMs, span.EndMs); }
        var ranges = coverage.Ranges.ToArray();
        var roster = value.Roster.Distinct().Order().ToArray();
        if (roster.Any(kind => !Enum.IsDefined(kind))) throw new InvalidDataException("Invalid instrument roster.");
        var points = new List<InstrumentScorePoint>();
        foreach (var point in value.Scores)
        {
            if (point is null) throw new InvalidDataException("Missing instrument score point.");
            CheckCovered(point.StartMs, point.EndMs, value.Recording.DurationMs, ranges);
            if (point.Scores is null || point.Scores.Count == 0 || point.Scores.Any(item => !Enum.IsDefined(item.Key)
                || !float.IsFinite(item.Value) || item.Value is < 0 or > 1)) throw new InvalidDataException("Invalid instrument scores.");
            points.Add(point with { Scores = point.Scores.OrderBy(item => item.Key).ToDictionary() });
        }
        var scores = points.OrderBy(point => point.StartMs).ThenBy(point => point.EndMs).ToArray();
        if (scores.Select(point => (point.StartMs, point.EndMs)).Distinct().Count() != scores.Length)
            throw new InvalidDataException("Duplicate score windows.");
        var intervals = new List<InstrumentInterval>();
        foreach (var item in value.Intervals)
        {
            if (item is null) throw new InvalidDataException("Missing instrument interval.");
            if (!Enum.IsDefined(item.Kind) || !roster.Contains(item.Kind)) throw new InvalidDataException("Interval is missing from the roster.");
            CheckCovered(item.StartMs, item.EndMs, value.Recording.DurationMs, ranges);
        }
        foreach (var kind in roster)
        {
            var union = new InstrumentCoverage();
            foreach (var item in value.Intervals.Where(item => item.Kind == kind)) union.Add(item.StartMs, item.EndMs);
            intervals.AddRange(union.Ranges.Select(span => new InstrumentInterval(kind, span.StartMs, span.EndMs)));
        }
        bool complete = coverage.IsComplete(value.Recording.DurationMs, Math.Min(50, value.Recording.DurationMs / 2d));
        if (value.Complete && !complete) throw new InvalidDataException("Claimed completeness does not match observed coverage.");
        return value with { Coverage = ranges, Scores = scores, Intervals = intervals.ToArray(), Roster = roster, Complete = complete };
    }

    internal static InstrumentTimeline Merge(InstrumentTimeline previous, InstrumentTimeline next)
    {
        var scores = previous.Scores.Where(point => !next.Coverage.Any(span => span.StartMs < point.EndMs && span.EndMs > point.StartMs))
            .Concat(next.Scores).ToArray();
        var intervals = new List<InstrumentInterval>();
        foreach (var item in previous.Intervals)
        {
            var retained = new List<InstrumentSpan> { new(item.StartMs, item.EndMs) };
            foreach (var cut in next.Coverage)
            {
                var parts = new List<InstrumentSpan>();
                foreach (var span in retained)
                {
                    if (cut.StartMs >= span.EndMs || cut.EndMs <= span.StartMs) parts.Add(span);
                    else
                    {
                        if (span.StartMs < cut.StartMs) parts.Add(new(span.StartMs, cut.StartMs));
                        if (span.EndMs > cut.EndMs) parts.Add(new(cut.EndMs, span.EndMs));
                    }
                }
                retained = parts;
            }
            intervals.AddRange(retained.Select(span => new InstrumentInterval(item.Kind, span.StartMs, span.EndMs)));
        }
        intervals.AddRange(next.Intervals);
        return Normalize(next with { Coverage = previous.Coverage.Concat(next.Coverage).ToArray(), Scores = scores,
            Intervals = intervals.ToArray(), Roster = previous.Roster.Concat(next.Roster).Distinct().ToArray() });
    }

    static void CheckRange(double start, double end, long duration)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start || end > duration)
            throw new InvalidDataException("Instrument time is outside the confirmed recording.");
    }
    static void CheckCovered(double start, double end, long duration, InstrumentSpan[] coverage)
    {
        CheckRange(start, end, duration);
        if (!coverage.Any(span => span.StartMs <= start && span.EndMs >= end))
            throw new InvalidDataException("Instrument evidence crosses an unobserved audio gap.");
    }
}
