using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyBatchLyricsAsync(string directory,Dictionary<string,object?> checks)
    {
        const string original="[00:01]We sail home\n[00:04]Across the quiet bay\n[00:08]The stars will guide us";
        const string translated="[00:01]我们驶向家乡\n[00:04]穿过宁静海湾\n[00:08]星光为我们引路";
        var hard=new Track("Own Batch A","Own Author","Own Album",10000);
        var fast=hard with{Title="Own Batch B"};
        var missing=hard with{Title="Own Batch C"};
        var calls=new ConcurrentQueue<string>();
        var fallbackStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFallback=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? priorSource=settings.LibraryPath;
        bool online=settings.OnlineLookup;
        SettingsWindow? preferences=null;
        using var source=new ResilientLyricsProvider(
            new LrclibProvider(new BatchHttpHandler(async (url,token)=>
            {
                calls.Enqueue(url);
                if(url.Contains("/get?",StringComparison.Ordinal))
                    return url.Contains(fast.Title,StringComparison.Ordinal)
                        ?BatchJson(new{id=5,trackName=fast.Title,artistName=fast.Artist,albumName=fast.Album,duration=10,syncedLyrics=translated})
                        :new HttpResponseMessage(HttpStatusCode.NotFound);
                if(url.Contains(hard.Title,StringComparison.Ordinal))
                {
                    fallbackStarted.TrySetResult();
                    await releaseFallback.Task.WaitAsync(token);
                }
                return BatchJson(Array.Empty<object>());
            }),minimumInterval:TimeSpan.Zero),
            new NetEaseTranslationProvider(new BatchHttpHandler((url,_)=>
            {
                calls.Enqueue(url);
                if(url.Contains("/song/lyric?",StringComparison.Ordinal))
                    return Task.FromResult(BatchJson(new{code=200,lrc=new{lyric=original},tlyric=new{lyric=translated}}));
                object[] matches=url.Contains(hard.Title,StringComparison.Ordinal)
                    ?[new{id=9,name=hard.Title,artists=new[]{new{name=hard.Artist}},album=new{name=hard.Album},duration=10000}]:[];
                return Task.FromResult(BatchJson(new{code=200,result=new{songs=matches}}));
            }),minimumInterval:TimeSpan.Zero));
        try
        {
            settings.LibraryPath="own-batch";
            settings.OnlineLookup=true;
            coordinator.WorkspaceOriginalProvider=source;
            string database=Path.Combine(coordinator.DataRoot,"lyrics.db");
            using(var repo=new LyricsRepository(database))
                repo.SyncLibrary(new("own-batch",4,[new("a",hard),new("b",fast),new("b-copy",fast),new("c",missing)],"own"));
            preferences=new(coordinator,id=>SelectUiTheme(id,false)){Owner=this,Width=980,Height=860};
            preferences.Show();preferences.SelectLibrarySongs();
            var panel=preferences.Library.Songs;
            await panel.Reload(true);
            await WaitAsync(()=>panel.SongList.Items.Count==4);
            panel.SelectKeys(panel.SongList.Items.Cast<LibraryManagedSong>().Select(s=>s.Key));
            panel.PreferExisting.IsChecked=true;
            Descendants<Button>(panel).Single(b=>b.Content?.ToString()==UiText.T("准备所选原词与译文"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await fallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitAsync(()=>panel.SongList.Items.Cast<LibraryManagedSong>().Count(s=>s.Track==fast && s.Status.Original==LibraryOriginalState.Ready)==2);
            var early=panel.SongList.Items.Cast<LibraryManagedSong>().ToArray();
            checks["batchLyrics.fastRowsVisibleBeforeFallbackReturns"]=coordinator.WorkspaceBusy && early.Count(s=>s.Track==fast && s.Status.Original==LibraryOriginalState.Ready)==2;
            checks["batchLyrics.pendingReasonVisible"]=early.Single(s=>s.Track==hard).Status.Reason=="精确查询未采用，等待补查。"
                && Descendants<TextBlock>(panel.SongList).Any(t=>t.Text==UiText.T("精确查询未采用，等待补查。"));
            checks["batchLyrics.fastPassBeforeAnyFallback"]=calls.Take(3).All(IsExactRequest) && calls.Count==4;
            checks["batchLyrics.selectionRetained"]=panel.SelectedSongs.Count==4;
            using(var repo=new LyricsRepository(database,true))checks["batchLyrics.pendingIsNotAnOriginal"]=repo.FindReadOnly(hard) is null;
            VisualVerification.Save(preferences,Path.Combine(directory,"batch-fast-pass.png"));
            releaseFallback.TrySetResult();
            await WaitAsync(()=>!coordinator.WorkspaceBusy && panel.SongList.Items.Cast<LibraryManagedSong>().Any(s=>s.Track==missing && s.Status.Reason=="来源未返回歌词候选"));
            using(var repo=new LyricsRepository(database,true))
            using(var translations=new TranslationRepository(coordinator.DataRoot,true))
            {
                checks["batchLyrics.fallbackAndFastOriginalsExact"]=repo.FindReadOnly(hard)?.Raw==original && repo.FindReadOnly(fast)?.Raw==translated;
                var translation=translations.Find(hard,new LrcParser().Parse(original));
                checks["batchLyrics.reusedTranslationBoundToOriginal"]=translation?.SourceHash==TranslationAlignment.Hash(original) && translation?.Lines.Count==3;
            }
            checks["batchLyrics.oneExactRequestPerRecording"]=calls.Count(IsExactRequest)==3;
            checks["batchLyrics.oneNetEaseSearchAndLyricForFallback"]=calls.Count(url=>url.Contains("/api/search/get?",StringComparison.Ordinal) && url.Contains(hard.Title,StringComparison.Ordinal))==1
                && calls.Count(url=>url.Contains("/song/lyric?",StringComparison.Ordinal))==1;
            checks["batchLyrics.pendingClearedOnCompletion"]=coordinator.WorkspacePendingLookup.Count==0;
            checks["batchLyrics.totalFixtureRequests"]=calls.Count;
            VisualVerification.Save(preferences,Path.Combine(directory,"batch-complete.png"));
        }
        finally
        {
            coordinator.PauseWorkspace();releaseFallback.TrySetResult();
            await WaitAsync(()=>!coordinator.WorkspaceBusy);
            coordinator.WorkspaceOriginalProvider=null;preferences?.Close();settings.LibraryPath=priorSource;settings.OnlineLookup=online;
        }

        static async Task WaitAsync(Func<bool> condition)
        {
            var limit=Environment.TickCount64+8000;
            while(!condition() && Environment.TickCount64<limit)await Task.Delay(25);
            if(!condition())throw new TimeoutException("Batch lyric fixture did not reach the expected state.");
        }
        static bool IsExactRequest(string url)=>url.StartsWith("https://lrclib.net/api/get?",StringComparison.Ordinal);
    }

    sealed class BatchHttpHandler(Func<string,CancellationToken,Task<HttpResponseMessage>> response):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            =>response(Uri.UnescapeDataString(request.RequestUri!.AbsoluteUri),token);
    }
    static HttpResponseMessage BatchJson(object value)=>new(HttpStatusCode.OK)
        {Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
}
