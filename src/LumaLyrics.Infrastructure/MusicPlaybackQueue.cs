using System.Globalization;
using System.Text;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed record MusicQueueEntry(Track Track,bool MetadataComplete=true,bool Inserted=false);
public sealed record MusicQueueSelection(MusicQueueSnapshot Snapshot,int Index);
public sealed record MusicQueueSnapshot(Track? Current,IReadOnlyList<MusicQueueEntry> Upcoming,bool Available,bool IsComplete,DateTimeOffset CapturedUtc,string Message)
{
    public static MusicQueueSnapshot Unavailable(string message)=>new(null,[],false,false,DateTimeOffset.UtcNow,message);
}

public static class MusicPlaybackQueue
{
    public static bool CanSelect(MusicQueueSelection selection)=>selection.Snapshot is {Available:true,Current:not null} snapshot
        && selection.Index>=0 && selection.Index<snapshot.Upcoming.Count && snapshot.Upcoming[selection.Index] is {MetadataComplete:true} entry
        && !string.IsNullOrWhiteSpace(entry.Track.Title) && !string.IsNullOrWhiteSpace(entry.Track.Artist);

    public static bool MatchesSelection(MusicQueueSelection selection,MusicQueueSnapshot actual)=>CanSelect(selection) && actual.Available
        && selection.Snapshot.Current==actual.Current && selection.Snapshot.IsComplete==actual.IsComplete
        && selection.Snapshot.Upcoming.SequenceEqual(actual.Upcoming);

    public static bool ConfirmsJump(MusicQueueSelection selection,MusicQueueSnapshot after)
    {
        if(!CanSelect(selection) || !after.Available || after.Current is null
            || !MusicLibraryPlayback.IsRequestedRecording(selection.Snapshot.Upcoming[selection.Index].Track,after.Current))return false;
        var remaining=selection.Snapshot.Upcoming.Skip(selection.Index+1).ToArray();
        // Repeated recordings need occurrence evidence: matching media metadata alone
        // cannot distinguish the first queued occurrence from a later one.
        return selection.Snapshot.IsComplete?after.IsComplete && remaining.SequenceEqual(after.Upcoming)
            :remaining.Length>0 && remaining.SequenceEqual(after.Upcoming.Take(remaining.Length));
    }

    // These are complete menu labels, never a prefix or a title search. In particular,
    // Play, Play Last and playlist actions cannot satisfy a Play Next request.
    public static bool IsPlayNextCommand(string command)=>new[]{"插播","Play Next","次に再生"}.Contains(command.Trim().Normalize(NormalizationForm.FormKC),StringComparer.OrdinalIgnoreCase);

    public static bool ConfirmsInsertion(MusicLibrarySong song,MusicQueueSnapshot before,MusicQueueSnapshot after)
    {
        bool Requested(MusicQueueEntry entry)=>entry.MetadataComplete && MusicLibraryPlayback.IsRequestedRecording(song.Track,entry.Track);
        if(!after.Available || after.Upcoming.FirstOrDefault() is not { } next || !Requested(next))return false;
        // If the recording was already first, merely reading the same old head is not
        // evidence that another occurrence was inserted successfully.
        return !before.Available || before.Upcoming.FirstOrDefault() is not { } old || !Requested(old)
            || before.IsComplete && after.IsComplete && after.Upcoming.Count(Requested)>before.Upcoming.Count(Requested);
    }

    // Apple Music's PlayQueueListView exposes title, artist—album, and duration as
    // separate text nodes. Preserve the supplied sequence, including repeated tracks.
    public static MusicQueueEntry? ParseNativeRow(IReadOnlyList<string> names)
    {
        if(names.Count<2 || string.IsNullOrWhiteSpace(names[0]) || string.IsNullOrWhiteSpace(names[1]))return null;
        double? duration=names.Skip(2).Select(n=>TimeSpan.TryParseExact(n,new[]{@"m\:ss",@"mm\:ss",@"h\:mm\:ss",@"hh\:mm\:ss"},CultureInfo.InvariantCulture,out var value)?(double?)value.TotalMilliseconds:null).FirstOrDefault(v=>v.HasValue);
        return new(TrackMetadata.FromAppleSession(new(names[0].Trim(),names[1].Trim(),"",duration)));
    }

    public static MusicLibrarySong? MatchLibrarySong(Track track,IReadOnlyList<MusicLibrarySong> songs)
    {
        var matches=songs.Where(s=>MusicLibraryPlayback.IsRequestedRecording(s.Track,track)).Take(2).ToArray();
        return matches.Length==1?matches[0]:null;
    }
}
