using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
        const string original="[00:01]ふるさとへ船をこぐ\n[00:04]静かな湾を渡って\n[00:08]星が道を照らす";
        const string translated="[00:01]我们驶向家乡\n[00:04]穿过宁静海湾\n[00:08]星光为我们引路";
        var hard=new Track("Kaze no Oto","Sora Hikari","Kotoba no Umi - Single",10000);
        var kana=new Track("カゼノオト","ソラヒカリ","コトバノウミ",9800);
        var fast=hard with{Title="Own Batch B"};
        var missing=hard with{Title="Own Batch C"};
        var backgroundOnly=hard with{Title="Own Background D"};
        var calls=new ConcurrentQueue<string>();
        var fallbackStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFallback=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prefetchStarted=new TaskCompletionSource<Track>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prefetchInterrupted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prefetchResumed=new TaskCompletionSource<Track>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrefetch=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int prefetchRequests=0;
        string? priorSource=settings.LibraryPath;
        bool online=settings.OnlineLookup,priorAutoPrefetch=settings.AutoPrefetchLibrary;
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
                object[] matches=url.Contains(kana.Title+" "+kana.Artist,StringComparison.Ordinal)
                    ?[new{id=9,name=kana.Title,artists=new[]{new{name=kana.Artist}},album=new{name=kana.Album},duration=kana.DurationMs}]:[];
                return Task.FromResult(BatchJson(new{code=200,result=new{songs=matches}}));
            }),minimumInterval:TimeSpan.Zero));
        try
        {
            string fixtureDirectory=Path.Combine(coordinator.DataRoot,"batch-fixture");
            Directory.CreateDirectory(fixtureDirectory);
            settings.LibraryPath=Path.Combine(fixtureDirectory,"Library.musicdb");
            WriteBatchLibrary(settings.LibraryPath,[hard,fast,fast,missing,backgroundOnly]);
            settings.OnlineLookup=true;
            settings.AutoPrefetchLibrary=true;
            coordinator.WorkspaceOriginalProvider=source;
            coordinator.LibraryPrefetchProvider=new BatchPrefetchProvider(async (track,token)=>
            {
                if(Interlocked.Increment(ref prefetchRequests)==1)
                {
                    prefetchStarted.TrySetResult(track);
                    try{await Task.Delay(Timeout.Infinite,token);}
                    finally{if(token.IsCancellationRequested)prefetchInterrupted.TrySetResult();}
                }
                else
                {
                    prefetchResumed.TrySetResult(track);
                    await releasePrefetch.Task.WaitAsync(token);
                }
                return [];
            });
            string database=Path.Combine(coordinator.DataRoot,"lyrics.db");
            string pausePath=LibraryPrefetcher.PausePath(database);
            using(var repo=new LyricsRepository(database))
                repo.SyncLibrary(new AppleMusicLibraryReader().Read(settings.LibraryPath));
            preferences=new(coordinator,id=>SelectUiTheme(id,false)){Owner=this,Width=980,Height=860};
            preferences.Show();preferences.SelectLibrarySongs();
            var panel=preferences.Library.Songs;
            await panel.Reload(true);
            await WaitAsync(()=>panel.SongList.Items.Count==5);
            panel.SelectKeys(panel.SongList.Items.Cast<LibraryManagedSong>().Where(s=>s.Track!=backgroundOnly).Select(s=>s.Key));
            panel.PreferExisting.IsChecked=true;
            coordinator.StartLibraryPrefetch();
            checks["batchLyrics.prefetchHeldLeaseBeforeWorkspace"]=await prefetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5))==hard && LibraryPrefetcher.IsRunning(database);
            Descendants<Button>(panel).Single(b=>b.Content?.ToString()==UiText.T("准备所选原词与译文"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await fallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitAsync(()=>panel.SongList.Items.Cast<LibraryManagedSong>().Count(s=>s.Track==fast && s.Status.Original==LibraryOriginalState.Ready)==2);
            // The refreshed items can arrive before WPF creates their row visuals.
            panel.SongList.UpdateLayout();
            var early=panel.SongList.Items.Cast<LibraryManagedSong>().ToArray();
            checks["batchLyrics.fastRowsVisibleBeforeFallbackReturns"]=coordinator.WorkspaceBusy && early.Count(s=>s.Track==fast && s.Status.Original==LibraryOriginalState.Ready)==2;
            checks["batchLyrics.pendingReasonVisible"]=early.Single(s=>s.Track==hard).Status.Reason=="精确查询未采用，等待补查。"
                && Descendants<TextBlock>(panel.SongList).Any(t=>t.Text==UiText.T("精确查询未采用，等待补查。"));
            checks["batchLyrics.fastPassBeforeAnyFallback"]=calls.Take(3).All(IsExactRequest) && calls.Count==4;
            checks["batchLyrics.selectionRetained"]=panel.SelectedSongs.Count==4;
            checks["batchLyrics.prefetchInterruptedWithoutPauseFile"]=prefetchInterrupted.Task.IsCompletedSuccessfully && !File.Exists(pausePath);
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
            checks["batchLyrics.originalAndKanaQueryOnceEach"]=calls.Count(url=>url.Contains("/api/search/get?",StringComparison.Ordinal) && url.Contains(hard.Title,StringComparison.Ordinal))==1
                && calls.Count(url=>url.Contains("/api/search/get?",StringComparison.Ordinal) && url.Contains(kana.Title+" "+kana.Artist,StringComparison.Ordinal))==1
                && calls.Count(url=>url.Contains("/song/lyric?",StringComparison.Ordinal))==1;
            checks["batchLyrics.pendingClearedOnCompletion"]=coordinator.WorkspacePendingLookup.Count==0;
            checks["batchLyrics.totalFixtureRequests"]=calls.Count;
            VisualVerification.Save(preferences,Path.Combine(directory,"batch-complete.png"));
            checks["batchLyrics.prefetchResumedAfterWorkspace"]=await prefetchResumed.Task.WaitAsync(TimeSpan.FromSeconds(5))==backgroundOnly
                && !File.Exists(pausePath) && LibraryPrefetcher.IsRunning(database);
            releasePrefetch.TrySetResult();
            await WaitAsync(()=>!LibraryPrefetcher.IsRunning(database));

            var missingRow=panel.SongList.Items.Cast<LibraryManagedSong>().Single(s=>s.Track==missing);
            int requestsBeforeConflict=calls.Count;
            using(var lease=new FileStream(Path.Combine(coordinator.DataRoot,"library-prefetch.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                const string message="另一个浮光歌词进程正在后台预缓存，请稍候或先暂停后台预缓存。";
                await coordinator.PrepareWorkspaceAsync([missingRow],false,true);
                await WaitAsync(()=>coordinator.WorkspaceMessage==message);
                checks["batchLyrics.originalLeaseConflictIsReported"]=calls.Count==requestsBeforeConflict && Descendants<TextBlock>(panel).Any(t=>t.Text==UiText.Status(message));
            }
            using(var lease=new FileStream(Path.Combine(coordinator.DataRoot,"translation-preparation.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                const string message="后台译文准备正在运行，请稍候或先暂停。";
                await coordinator.PrepareWorkspaceAsync([missingRow],false,true,prepareOriginal:false);
                await WaitAsync(()=>coordinator.WorkspaceMessage==message);
                checks["batchLyrics.translationLeaseConflictIsReported"]=calls.Count==requestsBeforeConflict && Descendants<TextBlock>(panel).Any(t=>t.Text==UiText.Status(message));
            }
            var afterConflicts=await coordinator.ReadWorkspaceAsync(default,true);
            checks["batchLyrics.leaseConflictsPreserveOriginalStatus"]=afterConflicts.Songs.Single(s=>s.Track==missing).Status==missingRow.Status;
        }
        finally
        {
            coordinator.PauseWorkspace();releaseFallback.TrySetResult();releasePrefetch.TrySetResult();
            settings.AutoPrefetchLibrary=priorAutoPrefetch;
            await WaitAsync(()=>!coordinator.WorkspaceBusy);
            coordinator.LibraryPrefetchProvider=null;coordinator.WorkspaceOriginalProvider=null;preferences?.Close();settings.LibraryPath=priorSource;settings.OnlineLookup=online;
        }

        static async Task WaitAsync(Func<bool> condition)
        {
            var limit=Environment.TickCount64+8000;
            while(!condition() && Environment.TickCount64<limit)await Task.Delay(25);
            if(!condition())throw new TimeoutException("Batch lyric fixture did not reach the expected state.");
        }
        static bool IsExactRequest(string url)=>url.StartsWith("https://lrclib.net/api/get?",StringComparison.Ordinal);
    }

    // A self-authored native library exercises the same prefetch worker and lease as the app.
    static void WriteBatchLibrary(string path,IReadOnlyList<Track> tracks)
    {
        using var payload=new MemoryStream();
        using var writer=new BinaryWriter(payload);
        void MetadataHeader(int kind,int length)
        {
            writer.Write("boma"u8);writer.Write(20);writer.Write(length);writer.Write(kind);writer.Write(0);
        }
        void Text(int kind,string value)
        {
            var bytes=Encoding.Unicode.GetBytes(value);
            MetadataHeader(kind,36+bytes.Length);writer.Write(1);writer.Write(bytes.Length);writer.Write(0L);writer.Write(bytes);
        }
        writer.Write("ltma"u8);writer.Write(12);writer.Write(tracks.Count);
        for(int i=0;i<tracks.Count;i++)
        {
            var track=tracks[i];
            writer.Write("itma"u8);writer.Write(24);writer.Write(0);writer.Write(4);writer.Write((ulong)(i+1));
            Text(2,track.Title);Text(3,track.Album);Text(4,track.Artist);
            MetadataHeader(1,180);writer.Write(new byte[156]);writer.Write((int)track.DurationMs!.Value);
        }
        writer.Write("lPma"u8);writer.Write(12);writer.Write(1);
        writer.Write("lpma"u8);writer.Write(48);writer.Write(48+52*tracks.Count);writer.Write(tracks.Count);writer.Write(tracks.Count);
        writer.Write(new byte[24]);writer.Write(1);
        for(int i=0;i<tracks.Count;i++)
        {
            MetadataHeader(218,52);writer.Write("IPfa"u8);writer.Write(32);writer.Write((ulong)(i+1));writer.Write((ulong)(i+1));writer.Write(0L);
        }
        writer.Flush();
        using var zipped=new MemoryStream();
        using(var zipper=new ZLibStream(zipped,CompressionLevel.Fastest,true))payload.WriteTo(zipper);
        var compressed=zipped.ToArray();int encrypted=compressed.Length-compressed.Length%16;
        var file=new byte[160+compressed.Length];
        "hfma"u8.CopyTo(file);BitConverter.GetBytes(160).CopyTo(file,4);BitConverter.GetBytes(file.Length).CopyTo(file,8);BitConverter.GetBytes(encrypted).CopyTo(file,84);
        using var aes=Aes.Create();aes.Key=Encoding.ASCII.GetBytes("BHUILuilfghuila3");
        aes.EncryptEcb(compressed.AsSpan(0,encrypted),file.AsSpan(160),PaddingMode.None);
        compressed.AsSpan(encrypted).CopyTo(file.AsSpan(160+encrypted));
        File.WriteAllBytes(path,file);
    }

    sealed class BatchPrefetchProvider(Func<Track,CancellationToken,Task<IReadOnlyList<LyricsCandidate>>> lookup):ILyricsProvider
    {
        public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)=>lookup(track,token);
    }
    sealed class BatchHttpHandler(Func<string,CancellationToken,Task<HttpResponseMessage>> response):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            =>response(Uri.UnescapeDataString(request.RequestUri!.AbsoluteUri),token);
    }
    static HttpResponseMessage BatchJson(object value)=>new(HttpStatusCode.OK)
        {Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
}
