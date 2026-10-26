using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using FlaUI.UIA3;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    // Isolated WPF verifier only; reject foreign windows so this fixture adapter
    // cannot become an alternative path for controlling the user's applications.
    internal static async Task<(string? Failure,MusicPlaybackModeState State,bool Attempted)> VerifyPlaylistModeFixtureAsync(nint handle,MusicPlaybackOptions options,CancellationToken token,bool ownsForeground=true)
    {
        using var process=Process.GetCurrentProcess();
        var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);bounded.CancelAfter(TimeSpan.FromSeconds(12));
        var worker=Task.Run(()=>
        {
            gate.Wait(bounded.Token);
            try
            {
                using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};
                var root=automation.FromHandle(handle);if(root.Properties.ProcessId.ValueOrDefault!=process.Id)throw new InvalidOperationException("Fixture must belong to this verification process.");
                var context=new HandoffContext{Window=new(ownsForeground?handle:0,process.Id,false)};
                lock(windowStateGate)context.Version=windowOwners.Next();
                string? failure=ApplyNativePlaybackModes(root,options,bounded.Token,context,null,()=>ownsForeground);
                return (failure,ReadNativePlaybackModes(root,bounded.Token),context.PlaylistModeAttempted);
            }
            finally{gate.Release();}
        },bounded.Token);
        try{return await worker.WaitAsync(TimeSpan.FromSeconds(13));}
        finally{bounded.Cancel();if(worker.IsCompleted)bounded.Dispose();else _=worker.ContinueWith(t=>{_=t.Exception;bounded.Dispose();},TaskScheduler.Default);}
    }
}
public partial class MainWindow
{
    async Task VerifyPlaylistModeAdapterAsync(Dictionary<string,object?> checks)
    {
        int shuffleWrites=0,repeatWrites=0;CancellationTokenSource? cancelOnRepeat=null;
        var content=new StackPanel{Margin=new(20)};
        var shuffle=new ToggleButton{Content="随机播放",Width=180,Height=40};AutomationProperties.SetAutomationId(shuffle,"ShuffleButton");content.Children.Add(shuffle);
        var repeat=new Button{Content="不重复",Width=180,Height=40};AutomationProperties.SetAutomationId(repeat,"RepeatButton");AutomationProperties.SetName(repeat,"不重复");content.Children.Add(repeat);
        shuffle.Checked+=(_,_)=>shuffleWrites++;shuffle.Unchecked+=(_,_)=>shuffleWrites++;
        repeat.Click+=(_,_)=>{repeatWrites++;string label=AutomationProperties.GetName(repeat) switch{"不重复"=>"Repeat All","Repeat All"=>"Repeat One",_=>"不重复"};repeat.Content=label;AutomationProperties.SetName(repeat,label);cancelOnRepeat?.Cancel();};
        var fixture=new Window{Title="原创播放模式测试",Width=300,Height=220,Content=content,Owner=this,ShowInTaskbar=false};
        try
        {
            fixture.Show();fixture.Activate();await Task.Delay(80);nint handle=new WindowInteropHelper(fixture).Handle;
            var result=await AppleMusicLibraryHandoff.VerifyPlaylistModeFixtureAsync(handle,new(true,MusicRepeatMode.One),default);
            checks["playlistModes.realUiaControlsApplyAndReadBack"]=result.Failure is null && result.State==new MusicPlaybackModeState(true,MusicRepeatMode.One) && shuffleWrites==1 && repeatWrites==2;
            result=await AppleMusicLibraryHandoff.VerifyPlaylistModeFixtureAsync(handle,new(true,MusicRepeatMode.One),default);
            checks["playlistModes.alreadyRequestedStateDoesNotToggle"]=result.Failure is null && !result.Attempted && shuffleWrites==1 && repeatWrites==2;
            result=await AppleMusicLibraryHandoff.VerifyPlaylistModeFixtureAsync(handle,new(false,MusicRepeatMode.None),default,false);
            checks["playlistModes.lostForegroundBlocksMutation"]=result.Failure is not null && !result.Attempted && shuffleWrites==1 && repeatWrites==2;
            AutomationProperties.SetName(repeat,"Unrecognized repeat state");
            result=await AppleMusicLibraryHandoff.VerifyPlaylistModeFixtureAsync(handle,new(Repeat:MusicRepeatMode.List),default);
            checks["playlistModes.unknownNativeStateDoesNotGuess"]=result.Failure is not null && !result.Attempted && repeatWrites==2;
            AutomationProperties.SetName(repeat,"不重复");
            using var cancellation=new CancellationTokenSource();cancelOnRepeat=cancellation;bool cancelled=false;
            try{await AppleMusicLibraryHandoff.VerifyPlaylistModeFixtureAsync(handle,new(Repeat:MusicRepeatMode.One),cancellation.Token);}catch(OperationCanceledException){cancelled=true;}
            checks["playlistModes.cancelBetweenTransitionsStopsNextCommand"]=cancelled && repeatWrites==3;
            cancelOnRepeat=null;shuffle.IsChecked=false;int beforeNoop=shuffleWrites;
            RoutedEventHandler veto=(_,_)=>shuffle.IsChecked=false;shuffle.Checked+=veto;
            try
            {
                result=await AppleMusicLibraryHandoff.VerifyPlaylistModeFixtureAsync(handle,new(true),default);
                checks["playlistModes.unconfirmedToggleIsNotRepeated"]=result.Failure is not null && result.Attempted && shuffleWrites==beforeNoop+2 && result.State.Shuffle==false;
            }
            finally{shuffle.Checked-=veto;}

        }
        finally{cancelOnRepeat=null;fixture.Close();Activate();}
    }
}
