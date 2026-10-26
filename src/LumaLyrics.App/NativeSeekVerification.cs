using System.Diagnostics;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    internal static object InspectSeekSurface()
    {
        using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);
        if(process is null)return new{available=false};
        using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(2)};
        var root=automation.FromHandle(process.MainWindowHandle);
        return root.FindAllDescendants(cf=>cf.ByControlType(ControlType.Slider).Or(cf.ByControlType(ControlType.ProgressBar))).Select(e=>new{name=e.Properties.Name.ValueOrDefault,id=e.Properties.AutomationId.ValueOrDefault,range=e.Patterns.RangeValue.IsSupported,minimum=e.Patterns.RangeValue.IsSupported?e.Patterns.RangeValue.Pattern.Minimum.Value:0,maximum=e.Patterns.RangeValue.IsSupported?e.Patterns.RangeValue.Pattern.Maximum.Value:0,value=e.Patterns.RangeValue.IsSupported?e.Patterns.RangeValue.Pattern.Value.Value:0,readOnly=e.Patterns.RangeValue.IsSupported && e.Patterns.RangeValue.Pattern.IsReadOnly.Value}).ToArray();
    }
}
