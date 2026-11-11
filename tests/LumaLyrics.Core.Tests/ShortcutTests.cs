using LumaLyrics.Core;
namespace LumaLyrics.Core.Tests;

public sealed class ShortcutTests
{
    sealed class Backend : IShortcutBackend
    {
        public Dictionary<int,ShortcutGesture> Registered {get;}=[];
        public HashSet<(uint,uint)> External {get;}=[];
        public Func<int,ShortcutGesture,bool>? Reject;
        public bool Register(int id,ShortcutGesture gesture)
        {
            if(External.Contains((gesture.Modifiers,gesture.Key)) || Registered.Values.Any(g=>g.Modifiers==gesture.Modifiers && g.Key==gesture.Key) || Reject?.Invoke(id,gesture)==true)return false;
            Registered.Add(id,gesture);return true;
        }
        public void Unregister(int id)=>Registered.Remove(id);
    }
    static Dictionary<ShortcutAction,string> Defaults()=>KeyboardShortcuts.All.ToDictionary(d=>d.Action,d=>d.Default);
    [Theory]
    [InlineData("alt + control + space","Ctrl+Alt+Space")][InlineData("CTRL+shift+f6","Ctrl+Shift+F6")][InlineData("alt+9","Alt+9")]
    public void CanonicalSyntaxKeepsTheSameWindowsGesture(string value,string canonical){Assert.True(ShortcutGesture.TryParse(value,out var gesture));Assert.Equal(canonical,gesture.Text);}
    [Theory]
    [InlineData("A")][InlineData("Shift+A")][InlineData("Win+A")][InlineData("Ctrl+Alt+Delete")][InlineData("Alt+Tab")][InlineData("Ctrl+F12")][InlineData("Ctrl+Ctrl+A")][InlineData("Ctrl+Unknown")][InlineData("Ctrl++A")]
    public void UnsafeOrInvalidCombinationsAreRejected(string value)=>Assert.False(ShortcutGesture.TryParse(value,out _));
    [Fact]
    public void AllDefaultsAreValidAndDistinct()
    {
        var gestures=KeyboardShortcuts.All.Select(d=>{Assert.True(ShortcutGesture.TryParse(d.Default,out var g));return (g.Modifiers,g.Key);}).ToArray();Assert.Equal(gestures.Length,gestures.Distinct().Count());
    }
    [Fact]
    public void ConflictInspectionLeavesExistingBindingsUntouched()
    {
        var backend=new Backend();using var registry=new ShortcutRegistry(backend);var defaults=Defaults();registry.Apply(defaults);
        ShortcutGesture.TryParse("Ctrl+Alt+K",out var blocked);backend.External.Add((blocked.Modifiers,blocked.Key));var draft=Defaults();draft[ShortcutAction.Next]=blocked.Text;
        Assert.Equal(ShortcutIssue.Occupied,registry.Apply(draft).Single(r=>r.Action==ShortcutAction.Next).Issue);
        Assert.Equal(defaults[ShortcutAction.Next],registry.Active[ShortcutAction.Next].Text);Assert.Equal(8,backend.Registered.Count);
    }
    [Fact]
    public void AutomaticResolutionChangesOnlyConflictsAndNeverEnablesDisabledActions()
    {
        var backend=new Backend();using var registry=new ShortcutRegistry(backend);var plan=Defaults();plan[ShortcutAction.Previous]=plan[ShortcutAction.Next];plan[ShortcutAction.UnlockOverlay]="";
        ShortcutGesture.TryParse(plan[ShortcutAction.PlayPause],out var blocked);backend.External.Add((blocked.Modifiers,blocked.Key));
        var fixedPlan=registry.Suggest(plan);Assert.NotEqual(plan[ShortcutAction.PlayPause],fixedPlan[ShortcutAction.PlayPause]);Assert.Equal(plan[ShortcutAction.LyricsEarlier],fixedPlan[ShortcutAction.LyricsEarlier]);Assert.Equal("",fixedPlan[ShortcutAction.UnlockOverlay]);
        Assert.All(registry.Apply(fixedPlan),r=>Assert.True(r.Issue is ShortcutIssue.None or ShortcutIssue.Disabled));Assert.Equal(7,registry.Active.Count);
    }
    [Fact]
    public void SwappingRegisteredKeysAndDisablingActionsWorks()
    {
        var backend=new Backend();using var registry=new ShortcutRegistry(backend);var plan=Defaults();registry.Apply(plan);
        (plan[ShortcutAction.Previous],plan[ShortcutAction.Next])=(plan[ShortcutAction.Next],plan[ShortcutAction.Previous]);plan[ShortcutAction.ResetOffset]="";
        Assert.All(registry.Apply(plan),r=>Assert.True(r.Issue is ShortcutIssue.None or ShortcutIssue.Disabled));Assert.Equal(plan[ShortcutAction.Previous],registry.Active[ShortcutAction.Previous].Text);Assert.False(registry.Active.ContainsKey(ShortcutAction.ResetOffset));
        registry.Dispose();Assert.Empty(backend.Registered);
    }
    [Fact]
    public void RegistrationRaceRollsBackTheWholeDraft()
    {
        var backend=new Backend();using var registry=new ShortcutRegistry(backend);var plan=Defaults();registry.Apply(plan);
        plan[ShortcutAction.Next]="Ctrl+Alt+K";backend.Reject=(id,g)=>id==(int)ShortcutAction.Next && g.Text=="Ctrl+Alt+K";
        Assert.Contains(registry.Apply(plan),r=>r.Issue==ShortcutIssue.Occupied);Assert.Equal(Defaults()[ShortcutAction.Next],registry.Active[ShortcutAction.Next].Text);Assert.Equal(8,registry.Active.Count);
    }
    [Fact]
    public void LostPreviousRegistrationIsReportedWhenRollbackAlsoFails()
    {
        var backend=new Backend();using var registry=new ShortcutRegistry(backend);var plan=Defaults();registry.Apply(plan);
        plan[ShortcutAction.Next]="Ctrl+Alt+K";backend.Reject=(id,_)=>id==(int)ShortcutAction.Next;
        Assert.Contains(registry.Apply(plan),r=>r.Action==ShortcutAction.Next && r.Issue==ShortcutIssue.RegistrationFailed);
        Assert.False(registry.Active.ContainsKey(ShortcutAction.Next));Assert.Equal(7,registry.Active.Count);
    }
    [Fact]
    public void StartupKeepsUnconflictedActionsAvailable()
    {
        var backend=new Backend();using var registry=new ShortcutRegistry(backend);var plan=Defaults();ShortcutGesture.TryParse(plan[ShortcutAction.PlayPause],out var blocked);backend.External.Add((blocked.Modifiers,blocked.Key));
        Assert.Contains(registry.Apply(plan,true),r=>r.Issue==ShortcutIssue.Occupied);Assert.Equal(7,registry.Active.Count);
    }
}
