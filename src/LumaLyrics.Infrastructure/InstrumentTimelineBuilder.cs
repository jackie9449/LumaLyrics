using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

/// <summary>Accepts only timed results for one recording/profile; gaps remain explicit.</summary>
public sealed class InstrumentTimelineBuilder
{
    readonly InstrumentRecordingKey recording;
    readonly string profileId;
    readonly Dictionary<(double Start, double End), InstrumentTimeline> windows = [];
    readonly HashSet<InstrumentKind> roster = [];
    long revision;
    InstrumentEpoch? owner;
    public InstrumentTimelineBuilder(InstrumentRecordingKey recording, string profileId)
    {
        _ = InstrumentTimelineValidation.Normalize(new(recording, profileId, [], [], [], [], false, 0));
        this.recording = recording; this.profileId = profileId;
    }
    public void Add(InstrumentFrame frame, InstrumentInterval[] intervals)
    {
        if (frame.ProfileId != profileId || frame.StartMs is null || frame.EndMs is null
            || owner is not null && (owner.SessionId != frame.Epoch.SessionId || owner.Generation != frame.Epoch.Generation || owner.Revision != frame.Epoch.Revision))
            throw new InvalidDataException("Unbound, untimed or incompatible instrument result cannot be persisted.");
        var next = InstrumentTimelineValidation.Normalize(new(recording, profileId,
            [new(frame.StartMs.Value, frame.EndMs.Value)],
            [new(frame.StartMs.Value, frame.EndMs.Value, frame.Scores.ToDictionary())], intervals,
            intervals.Select(item => item.Kind).Distinct().Order().ToArray(), false, checked(revision + 1)));
        windows[(frame.StartMs.Value, frame.EndMs.Value)] = next;
        roster.UnionWith(next.Roster); revision++; owner = frame.Epoch;
    }
    public InstrumentTimeline Build() => InstrumentTimelineValidation.Normalize(new(recording, profileId,
        windows.Values.SelectMany(value => value.Coverage).ToArray(), windows.Values.SelectMany(value => value.Scores).ToArray(),
        windows.Values.SelectMany(value => value.Intervals).ToArray(), roster.Order().ToArray(), false, revision));
}
