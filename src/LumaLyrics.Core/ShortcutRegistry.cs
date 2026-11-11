namespace LumaLyrics.Core;

public enum ShortcutIssue { None,Disabled,Invalid,Duplicate,Occupied,RegistrationFailed }
public sealed record ShortcutRegistration(ShortcutAction Action,string Binding,ShortcutIssue Issue);
public interface IShortcutBackend
{
    bool Register(int id,ShortcutGesture gesture);
    void Unregister(int id);
}

/// <summary>Owns only this application's registrations; never intercepts or unregisters another app's keys.</summary>
public sealed class ShortcutRegistry(IShortcutBackend backend) : IDisposable
{
    readonly Dictionary<ShortcutAction,ShortcutGesture> active=[];
    public IReadOnlyDictionary<ShortcutAction,ShortcutGesture> Active=>active;
    static bool Same(ShortcutGesture a,ShortcutGesture b)=>a.Modifiers==b.Modifiers && a.Key==b.Key;
    bool Available(ShortcutGesture gesture)
    {
        if(active.Values.Any(g=>Same(g,gesture)))return true;
        const int probeId=0x3FFE;
        if(!backend.Register(probeId,gesture))return false;
        backend.Unregister(probeId);return true;
    }
    public IReadOnlyList<ShortcutRegistration> Inspect(IReadOnlyDictionary<ShortcutAction,string> bindings)
    {
        var used=new HashSet<(uint,uint)>();var result=new List<ShortcutRegistration>();
        foreach(var item in KeyboardShortcuts.All)
        {
            string value=bindings.GetValueOrDefault(item.Action,"").Trim();
            var issue=ShortcutIssue.None;
            if(value.Length==0)issue=ShortcutIssue.Disabled;
            else if(!ShortcutGesture.TryParse(value,out var gesture))issue=ShortcutIssue.Invalid;
            else {value=gesture.Text;if(!used.Add((gesture.Modifiers,gesture.Key)))issue=ShortcutIssue.Duplicate;else if(!Available(gesture))issue=ShortcutIssue.Occupied;}
            result.Add(new(item.Action,value,issue));
        }
        return result;
    }
    public IReadOnlyList<ShortcutRegistration> Apply(IReadOnlyDictionary<ShortcutAction,string> bindings,bool keepAvailableOnFailure=false)
    {
        var inspected=Inspect(bindings);
        if(!keepAvailableOnFailure && inspected.Any(r=>r.Issue is not (ShortcutIssue.None or ShortcutIssue.Disabled)))return inspected;
        var previous=active.ToArray();Release();var result=new List<ShortcutRegistration>();
        foreach(var item in inspected)
        {
            if(item.Issue!=ShortcutIssue.None){result.Add(item);continue;}
            ShortcutGesture.TryParse(item.Binding,out var gesture);
            if(backend.Register((int)item.Action,gesture)){active[item.Action]=gesture;result.Add(item);}
            else result.Add(item with{Issue=ShortcutIssue.Occupied});
        }
        if(!keepAvailableOnFailure && result.Any(r=>r.Issue is not (ShortcutIssue.None or ShortcutIssue.Disabled)))
        {
            Release();
            foreach(var (action,gesture) in previous)
                if(backend.Register((int)action,gesture))active[action]=gesture;
                else {result.RemoveAll(r=>r.Action==action);result.Add(new(action,gesture.Text,ShortcutIssue.RegistrationFailed));}
        }
        return result;
    }
    public Dictionary<ShortcutAction,string> Suggest(IReadOnlyDictionary<ShortcutAction,string> bindings)
    {
        var plan=new Dictionary<ShortcutAction,string>();var used=new HashSet<(uint,uint)>();
        foreach(var definition in KeyboardShortcuts.All)
        {
            string value=bindings.GetValueOrDefault(definition.Action,"");
            if(string.IsNullOrWhiteSpace(value)){plan[definition.Action]="";continue;}
            if(ShortcutGesture.TryParse(value,out var original) && !used.Contains((original.Modifiers,original.Key)) && Available(original))
            {plan[definition.Action]=original.Text;used.Add((original.Modifiers,original.Key));continue;}
            var alternative=ShortcutGesture.Alternatives(ShortcutGesture.TryParse(value,out _)?value:definition.Default).FirstOrDefault(g=>!used.Contains((g.Modifiers,g.Key)) && Available(g));
            if(alternative.Text is not null){plan[definition.Action]=alternative.Text;used.Add((alternative.Modifiers,alternative.Key));}
            else plan[definition.Action]=value;
        }
        return plan;
    }
    void Release(){foreach(var action in active.Keys)backend.Unregister((int)action);active.Clear();}
    public void Dispose()=>Release();
}
