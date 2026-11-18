using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyLibraryManualLyricsAsync(string directory,Dictionary<string,object?> checks)
    {
        var playing=new Track("Own current song","Own performer","Own album",95000);
        var target=playing with{Title="Own lookup target"};
        var other=playing with{Title="Own other row"};
        const string original="[00:01]We walk through fields of evening light\n[00:12]The quiet hills are waiting";
        var candidate=new LyricsCandidate(4242,target with{Title="Own source alias"},original,null,false,"{}");
        var provider=new ManualLyricsFixtureProvider(candidate);
        var fixtureStore=new SettingsStore(Path.Combine(store.Root,"library-manual-fixture"));
        var fixtureSettings=new AppSettings{LibraryPath="own-manual",OnlineLookup=true,AutoPrefetchLibrary=false};
        await using var subject=new PlaybackCoordinator(Dispatcher,fixtureStore,fixtureSettings,provider);
        using var repository=new LyricsRepository(Path.Combine(fixtureStore.Root,"lyrics.db"));
        repository.SyncLibrary(new("own-manual",3,[new("a",playing),new("b",target),new("c",other)],"own"));
        subject.SetManualLyricsVerificationSnapshot(new("own-fixture",1,1,playing,PlayState.Paused,1,5000,
            DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,Stopwatch.GetTimestamp(),PositionQuality.Reliable));
        subject.Import(playing,"[00:01]Own playing lyrics","Own fixture",false);
        var originalBinding=subject.Binding;
        string originalStatus=subject.LyricsStatus;
        string theme=settings.UiTheme;
        var cursor=System.Windows.Forms.Cursor.Position;
        var preferences=new SettingsWindow(subject,id=>SelectUiTheme(id,false)){Owner=this,Width=980,Height=800};
        try
        {
            preferences.Show();preferences.SelectLibrarySongs();
            var panel=preferences.Library.Songs;
            await panel.Reload(true);
            await WaitFor(()=>panel.SongList.Items.Count==3);
            var rows=panel.SongList.Items.Cast<LibraryManagedSong>().ToArray();
            panel.SelectKeys(rows.Where(row=>row.Track!=target).Select(row=>row.Key));
            preferences.UpdateLayout();
            var targetItem=(ListBoxItem)panel.SongList.ItemContainerGenerator.ContainerFromItem(rows.Single(r=>r.Track==target));
            var menu=targetItem.ContextMenu!;
            preferences.Activate();
            await WaitFor(()=>ManualLyricsForegroundWindow()==new WindowInteropHelper(preferences).Handle);
            var center=targetItem.PointToScreen(new Point(targetItem.ActualWidth/2,targetItem.ActualHeight/2));
            System.Windows.Forms.Cursor.Position=new((int)center.X,(int)center.Y);
            await Task.Delay(40);
            if(ManualLyricsForegroundWindow()!=new WindowInteropHelper(preferences).Handle)throw new InvalidOperationException("The isolated Settings window lost input focus.");
            ManualLyricsMouseEvent(0x0008,0,0,0,0);
            ManualLyricsMouseEvent(0x0010,0,0,0,0);
            await WaitFor(()=>menu.IsOpen);
            var command=menu.Items.OfType<MenuItem>().Single();
            checks["libraryManual.nativeRightClickTarget"]=ReferenceEquals(menu.PlacementTarget,targetItem) && command.IsEnabled;
            checks["libraryManual.rightClickKeepsMultiSelection"]=panel.SelectedSongs.Select(row=>row.Track).ToHashSet().SetEquals([playing,other]);
            checks["libraryManual.themedMenu"]=menu.Style==FindResource("LibraryActionMenu") && command.Header?.ToString()==UiText.T("手动查词…");
            VisualVerification.Save(menu,Path.Combine(directory,"library-song-context-menu.png"));

            var interaction=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _=Dispatcher.BeginInvoke(async()=>
            {
                CandidateWindow? dialog=null;
                try
                {
                    await WaitFor(()=>Application.Current.Windows.OfType<CandidateWindow>().Any(w=>w.IsVisible));
                    dialog=Application.Current.Windows.OfType<CandidateWindow>().Single(w=>w.IsVisible);
                    var list=Descendants<ListBox>(dialog).Single();
                    await WaitFor(()=>list.Items.Count==1);
                    checks["libraryManual.prefilledSearchUsesClickedRecording"]=provider.LastQuery==target && provider.ManualCalls==1 && provider.AutomaticCalls==0;
                    checks["libraryManual.searchLeavesPlaybackState"]=subject.Binding==originalBinding && subject.LyricsStatus==originalStatus && subject.Candidates.Count==0;
                    var edited=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _=Dispatcher.BeginInvoke(()=>
                    {
                        var search=Application.Current.Windows.OfType<LyricsSearchWindow>().Single();
                        checks["libraryManual.editFormPrefillsRow"]=search.SongTitle.Text==target.Title && search.Artist.Text==target.Artist && search.Album.Text==target.Album;
                        search.SongTitle.Text="Own alternate keyword";
                        search.Artist.Clear();search.Album.Clear();search.Duration.Clear();
                        Button(search,"搜索").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        edited.SetResult();
                    });
                    Button(dialog,"手动填写歌名 / 歌手搜索").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    await edited.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    await WaitFor(()=>list.Items.Count==1);
                    checks["libraryManual.editedQueryKeepsOriginalTarget"]=provider.LastQuery is {Title:"Own alternate keyword",Artist:""}
                        && Descendants<TextBlock>(dialog).Any(t=>t.Text==UiText.F($"绑定目标：{target.Title} · {target.Artist}\n{target.Album}"));
                    list.SelectedIndex=0;
                    Button(dialog,"使用所选歌词").BringIntoView();
                    await Task.Delay(100);
                    VisualVerification.Save(dialog,Path.Combine(directory,"library-manual-candidates.png"));
                    Button(dialog,"使用所选歌词").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    interaction.SetResult();
                }
                catch(Exception e){dialog?.Close();interaction.SetException(e);}
            });
            command.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await interaction.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var saved=repository.FindReadOnly(target);
            checks["libraryManual.boundOnlyToClickedRow"]=saved is {Manual:true} && saved.Raw==original
                && repository.FindReadOnly(other) is null && repository.FindReadOnly(target with{Title="Own alternate keyword"}) is null;
            checks["libraryManual.confirmLeavesOtherPlaybackUntouched"]=subject.Binding==originalBinding && subject.Snapshot?.Track==playing;
            await WaitFor(()=>panel.SongList.Items.Cast<LibraryManagedSong>().Any(r=>r.Track==target && r.BindingId==saved?.Id));
            checks["libraryManual.workspaceRereadsBinding"]=panel.SongList.Items.Cast<LibraryManagedSong>().Single(r=>r.Track==target).Status.Original==LibraryOriginalState.Ready;

            var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.Handler=async (_,token)=>
            {
                pending.SetResult();
                try{await Task.Delay(Timeout.Infinite,token);return [];}
                finally{if(token.IsCancellationRequested)cancelled.TrySetResult();}
            };
            var cancellationDialog=new CandidateWindow(subject,rows.Single(r=>r.Track==other).Target){Owner=preferences};
            _=Dispatcher.BeginInvoke(async()=>{await pending.Task;cancellationDialog.Close();});
            cancellationDialog.ShowDialog();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(4));
            checks["libraryManual.closeCancelsOnlyItsSearch"]=repository.FindReadOnly(other) is null && subject.Binding==originalBinding;

            provider.Handler=null;
            var liveRow=panel.SongList.Items.Cast<LibraryManagedSong>().Single(r=>r.Track==playing);
            subject.SelectLibraryCandidate(liveRow.Target,candidate);
            checks["libraryManual.confirmedPlayingRecordingUpdates"]=subject.Binding is {Manual:true} && subject.Binding.Id!=originalBinding?.Id && subject.Document?.Raw==original;
        }
        finally{System.Windows.Forms.Cursor.Position=cursor;preferences.Close();SelectUiTheme(theme,false);}

        static Button Button(Window window,string label)=>Descendants<Button>(window).Single(b=>b.Content?.ToString()==UiText.T(label));
        static async Task WaitFor(Func<bool> ready)
        {
            long end=Environment.TickCount64+5000;
            while(!ready() && Environment.TickCount64<end)await Task.Delay(25);
            if(!ready())throw new TimeoutException("The isolated manual lyrics UI did not reach the expected state.");
        }
    }

    [DllImport("user32.dll",EntryPoint="GetForegroundWindow")]
    static extern IntPtr ManualLyricsForegroundWindow();
    [DllImport("user32.dll",EntryPoint="mouse_event")]
    static extern void ManualLyricsMouseEvent(uint flags,uint x,uint y,uint data,nuint extra);
}
