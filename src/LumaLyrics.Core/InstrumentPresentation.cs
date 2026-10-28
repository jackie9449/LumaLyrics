using System.Diagnostics;

namespace LumaLyrics.Core;

/// <summary>One session's stable roster. Confidence is never presented as loudness.</summary>
public sealed class InstrumentPresentation
{
    readonly IReadOnlyDictionary<InstrumentKind, ActivityParameters> parameters;
    readonly Dictionary<InstrumentKind, InstrumentActivityFilter> filters = [];
    readonly Dictionary<InstrumentKind, InstrumentActivityFilter> admission = [];
    readonly SortedSet<InstrumentKind> roster = [];
    readonly Dictionary<InstrumentKind, bool> active = [];
    InstrumentEpoch? epoch;
    long lastTicks = -1;
    readonly double freshnessMs;
    public IReadOnlyList<InstrumentKind> ConfirmedRoster => roster.ToArray();
    public InstrumentPresentation(IReadOnlyDictionary<InstrumentKind, ActivityParameters> parameters,
        IReadOnlyDictionary<InstrumentKind, ActivityParameters>? rosterParameters = null, double freshnessMs = 2000)
    {
        if (!double.IsFinite(freshnessMs) || freshnessMs <= 0) throw new ArgumentOutOfRangeException(nameof(freshnessMs));
        this.freshnessMs = freshnessMs;
        this.parameters = parameters.ToDictionary();
        foreach (var pair in parameters) { if (!Enum.IsDefined(pair.Key)) throw new ArgumentException("Invalid instrument family."); filters[pair.Key] = new(pair.Value); }
        rosterParameters ??= parameters;
        if (!rosterParameters.Keys.Order().SequenceEqual(parameters.Keys.Order())) throw new ArgumentException("Roster rules must cover the same instruments.");
        foreach (var pair in rosterParameters) admission[pair.Key] = new(pair.Value);
    }
    public void Begin(InstrumentEpoch next, bool retainRoster)
    {
        epoch = next; lastTicks = -1; active.Clear();
        foreach (var filter in filters.Values) filter.Reset();
        foreach (var filter in admission.Values) filter.Reset();
        if (!retainRoster) roster.Clear();
    }
    public bool Apply(InstrumentFrame frame)
    {
        if (frame.Epoch != epoch || frame.EndTicks <= lastTicks) return false;
        if (frame.Scores.Any(pair => !parameters.ContainsKey(pair.Key) || !float.IsFinite(pair.Value) || pair.Value is < 0 or > 1))
            throw new InvalidDataException("Invalid instrument presentation scores.");
        if (lastTicks >= 0 && Stopwatch.GetElapsedTime(lastTicks, frame.EndTicks).TotalMilliseconds > freshnessMs)
        {
            foreach (var filter in filters.Values) filter.Reset();
            foreach (var filter in admission.Values) filter.Reset();
        }
        active.Clear();
        foreach (var kind in parameters.Keys.Except(frame.Scores.Keys)) { filters[kind].Reset(); admission[kind].Reset(); }
        foreach (var pair in frame.Scores)
        {
            bool value = filters[pair.Key].Update(pair.Value, frame.EndTicks * (1000d / Stopwatch.Frequency));
            active[pair.Key] = value;
            if (admission[pair.Key].Update(pair.Value, frame.EndTicks * (1000d / Stopwatch.Frequency))) roster.Add(pair.Key);
        }
        lastTicks = frame.EndTicks; return true;
    }
    public InstrumentViewState View(PlayState state, long nowTicks)
    {
        bool paused = state != PlayState.Playing;
        bool fresh = lastTicks >= 0 && nowTicks >= lastTicks && Stopwatch.GetElapsedTime(lastTicks, nowTicks).TotalMilliseconds <= freshnessMs;
        var icons = InstrumentVocabulary.VisibleKinds(roster).ToDictionary(kind => kind, kind => paused ? InstrumentVisualState.Dim
            : !fresh || !active.TryGetValue(kind, out _) ? InstrumentVisualState.Unknown
            : active[kind] ? InstrumentVisualState.Active : InstrumentVisualState.Dim);
        string message = paused ? "已暂停" : !fresh ? "等待新的音频结果"
            : icons.Count == 0 ? "暂未识别出支持的乐器" : "实时识别 · 清单逐步完善";
        return new(paused ? InstrumentStatus.Paused : InstrumentStatus.Learning, false, icons, message);
    }

    public static InstrumentViewState FromTimeline(InstrumentTimeline timeline, double? positionMs, PlayState state)
    {
        bool paused = state != PlayState.Playing;
        bool covered = positionMs is { } position && double.IsFinite(position)
            && timeline.Coverage.Any(span => span.StartMs <= position && position < span.EndMs);
        var icons = InstrumentVocabulary.VisibleKinds(timeline.Roster).ToDictionary(kind => kind, kind => paused ? InstrumentVisualState.Dim
            : !covered || !timeline.Scores.Any(point => point.StartMs <= positionMs && positionMs < point.EndMs && point.Scores.ContainsKey(kind)) ? InstrumentVisualState.Unknown
            : timeline.Intervals.Any(interval => interval.Kind == kind && interval.StartMs <= positionMs && positionMs < interval.EndMs)
                ? InstrumentVisualState.Active : InstrumentVisualState.Dim);
        return new(paused ? InstrumentStatus.Paused : timeline.Complete ? InstrumentStatus.Ready : InstrumentStatus.Learning,
            timeline.Complete, icons, paused ? "已暂停" : timeline.Complete ? "已分析整曲 · 识别到的乐器" : "乐器清单尚未完整");
    }
}
