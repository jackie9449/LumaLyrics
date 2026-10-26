using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public enum MusicRepeatMode { None,List,One }
public sealed record MusicPlaybackOptions(bool? Shuffle=null,MusicRepeatMode? Repeat=null);
public sealed record MusicPlaybackModeState(bool? Shuffle,MusicRepeatMode? Repeat);
// Null index invokes native whole-playlist Play. A non-null index captures an exact
// occurrence in the original unfiltered playlist, including repeated recordings.
public sealed record MusicPlaylistSelection(MusicPlaylist Playlist,int? Index=null,MusicPlaybackOptions? Options=null);

public static class MusicPlaybackModeRules
{
    public static bool Valid(MusicPlaybackOptions? options)=>options?.Repeat is not { } repeat || Enum.IsDefined(repeat);
    public static bool Matches(MusicPlaybackOptions? options,MusicPlaybackModeState state)=>Valid(options)
        && (options?.Shuffle is not { } shuffle || state.Shuffle==shuffle)
        && (options?.Repeat is not { } repeat || state.Repeat==repeat);
    public static MusicRepeatMode? ParseRepeat(string name)=>name.Trim() switch
    {
        "不重复" or "不循环" or "Repeat Off" or "Repeat off" or "No Repeat" or "Off"=>MusicRepeatMode.None,
        "重复所有" or "全部重复" or "重复全部" or "重复所有项目" or "重复播放所有项目" or "Repeat All" or "Repeat all" or "All"=>MusicRepeatMode.List,
        "重复单曲" or "单曲重复" or "重复单个项目" or "重复播放单曲" or "Repeat One" or "Repeat one" or "One"=>MusicRepeatMode.One,
        _=>null
    };
}
public static class MusicPlaylistContinuation
{
    public static bool CanSelect(MusicPlaylistSelection selection)=>selection.Playlist.CanPlay
        && selection.Playlist.Songs.Count==selection.Playlist.DeclaredCount && MusicPlaybackModeRules.Valid(selection.Options)
        && (selection.Index is null || selection.Index>=0 && selection.Index<selection.Playlist.Songs.Count);
    public static bool MatchesCurrent(MusicPlaylistSelection selection,MusicPlaybackModeState modes,Track actual)
    {
        if(!CanSelect(selection) || modes.Shuffle is null || !MusicPlaybackModeRules.Matches(selection.Options,modes))return false;
        return selection.Index is { } index?MusicLibraryPlayback.IsRequestedRecording(selection.Playlist.Songs[index].Track,actual)
            :modes.Shuffle==true?selection.Playlist.Songs.Any(s=>MusicLibraryPlayback.IsRequestedRecording(s.Track,actual))
            :MusicLibraryPlayback.IsRequestedRecording(selection.Playlist.Songs[0].Track,actual);
    }
    public static bool Confirms(MusicPlaylistSelection selection,MusicPlaybackModeState modes,MusicQueueSnapshot after)
    {
        if(!after.Available || after.Current is null || !MatchesCurrent(selection,modes,after.Current) || after.Upcoming.Any(e=>!e.MetadataComplete))return false;
        var songs=selection.Playlist.Songs;
        if(modes.Shuffle==true)
        {
            // A partial random queue cannot prove that every remaining occurrence was
            // retained. Require a complete multiset, never just matching membership.
            if(!after.IsComplete || after.Upcoming.Count!=songs.Count-1)return false;
            var pool=songs.Select(s=>s.Track).ToList();
            int chosen=selection.Index??pool.FindIndex(t=>MusicLibraryPlayback.IsRequestedRecording(t,after.Current));
            if(chosen<0)return false;pool.RemoveAt(chosen);
            foreach(var entry in after.Upcoming)
            {
                int index=pool.FindIndex(t=>MusicLibraryPlayback.IsRequestedRecording(t,entry.Track));
                if(index<0)return false;pool.RemoveAt(index);
            }
            return pool.Count==0;
        }
        int start=selection.Index??0;var suffix=songs.Skip(start+1).Select(s=>s.Track).ToArray();
        bool Sequence(Track[] expected)=>after.Upcoming.Count<=expected.Length
            && (after.IsComplete?after.Upcoming.Count==expected.Length:expected.Length>0 && after.Upcoming.Count>=Math.Min(10,expected.Length))
            && after.Upcoming.Select((e,index)=>MusicLibraryPlayback.IsRequestedRecording(expected[index],e.Track)).All(b=>b);
        if(Sequence(suffix))return true;
        // Some native versions expose the upcoming repeat wrap explicitly. Accept
        // it only when the native repeat state confirms All and every position matches.
        return modes.Repeat==MusicRepeatMode.List && Sequence(suffix.Concat(songs.Take(start+1).Select(s=>s.Track)).ToArray());
    }
}
