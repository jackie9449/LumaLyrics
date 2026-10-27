using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LumaLyrics.Core;
using ShapePath = System.Windows.Shapes.Path;

namespace LumaLyrics.App;

/// <summary>Main-window resources only. No floating lyric styles or brushes are modified.</summary>
public sealed class InstrumentActivityView : UserControl, IDisposable
{
    readonly TextBlock caption = new() { FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 0, 0, 5) };
    readonly StackPanel icons = new() { Orientation = Orientation.Horizontal };
    readonly Dictionary<InstrumentKind, Chip> chips = [];
    string signature = "";
    bool disposed;
    public InstrumentActivityView()
    {
        ClipToBounds = true; MaxHeight = 82; Margin = new(0, 7, 0, 0);
        var root = new StackPanel(); ThemeManager.Bind(caption, TextBlock.ForegroundProperty, "MutedBrush"); root.Children.Add(caption);
        root.Children.Add(new ScrollViewer { Content = icons, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false, MaxHeight = 64 });
        Content = root; Unloaded += Unload; IsVisibleChanged += VisibilityChanged;
    }
    public void Apply(InstrumentViewState state)
    {
        if (disposed) return;
        string next = state.Status + "|" + state.Message + "|" + string.Join(',', state.Icons.OrderBy(pair => pair.Key).Select(pair => pair.Key + ":" + pair.Value));
        if (next == signature) return; signature = next;
        Visibility = state.Status == InstrumentStatus.Disabled ? Visibility.Collapsed : Visibility.Visible;
        caption.Text = state.Message; ToolTip = "实验识别 · " + state.Message;
        foreach (var key in chips.Keys.Except(state.Icons.Keys).ToArray())
        { chips[key].Finish(); icons.Children.Remove(chips[key].Root); chips.Remove(key); }
        foreach (var pair in state.Icons.OrderBy(pair => pair.Key))
        {
            if (!chips.TryGetValue(pair.Key, out var chip))
            {
                chip = new(pair.Key); chips[pair.Key] = chip;
                int index = state.Icons.Keys.Order().ToList().IndexOf(pair.Key); icons.Children.Insert(Math.Min(index, icons.Children.Count), chip.Root);
            }
            chip.Apply(pair.Value, IsLoaded && IsVisible && AppMotion.Allowed && AppMotion.GetAnimationsEnabled(this));
        }
        AutomationProperties.SetName(this, "乐器识别：" + state.Message);
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == AppMotion.AnimationsEnabledProperty && e.NewValue is false && chips is not null)
            foreach (var chip in chips.Values) chip.Finish();
    }
    void Unload(object sender, RoutedEventArgs e) { foreach (var chip in chips.Values) chip.Finish(); }
    void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) { if (e.NewValue is false) foreach (var chip in chips.Values) chip.Finish(); }
    public void Dispose() { if (disposed) return; disposed = true; Unloaded -= Unload; IsVisibleChanged -= VisibilityChanged; foreach (var chip in chips.Values) chip.Finish(); }
    internal int ActiveClockCount => chips.Values.Count(chip => chip.Drawing.HasAnimatedProperties);

    sealed class Chip
    {
        public Border Root { get; } = new() { Width = 54, Margin = new(0, 0, 5, 0), Padding = new(3, 3, 3, 2), CornerRadius = new(7), BorderThickness = new(1) };
        public ShapePath Drawing { get; }
        readonly TextBlock text;
        readonly string name;
        long version;
        public Chip(InstrumentKind kind)
        {
            name = InstrumentIcons.Name(kind);
            Drawing = new() { Data = InstrumentIcons.For(kind), Width = 20, Height = 20, Stretch = Stretch.Uniform, StrokeThickness = 1.5 };
            text = new() { Text = name, FontSize = 9, TextAlignment = TextAlignment.Center, Margin = new(0, 2, 0, 0) };
            var panel = new StackPanel(); panel.Children.Add(Drawing); panel.Children.Add(text); Root.Child = panel;
        }
        public void Apply(InstrumentVisualState state, bool animate)
        {
            string brush = state == InstrumentVisualState.Active ? "AccentBrush" : "MutedBrush";
            ThemeManager.Bind(Drawing, ShapePath.StrokeProperty, brush); ThemeManager.Bind(text, TextBlock.ForegroundProperty, brush);
            ThemeManager.Bind(Root, Border.BorderBrushProperty, state == InstrumentVisualState.Active ? "AccentBrush" : "BorderBrush");
            Root.SetResourceReference(Border.BackgroundProperty, state == InstrumentVisualState.Active ? "AccentSoftBrush" : "PanelBrush");
            Drawing.StrokeDashArray = state == InstrumentVisualState.Unknown ? new DoubleCollection { 2, 2 } : null;
            string description = state == InstrumentVisualState.Active ? "正在演奏（模型判断）" : state == InstrumentVisualState.Dim ? "本曲包含，当前未活动" : "当前区间未识别";
            AutomationProperties.SetName(Root, name + "，" + description); Root.ToolTip = name + " · " + description;
            AutomationProperties.SetName(text, name + "，" + description);
            double from = Drawing.Opacity, to = state == InstrumentVisualState.Active ? 1 : state == InstrumentVisualState.Dim ? .38 : .7;
            Finish(); Drawing.Opacity = to;
            if (!animate || Math.Abs(from - to) < .01) return;
            long current = ++version;
            var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(120)) { FillBehavior = FillBehavior.Stop };
            animation.Completed += (_, _) => { if (current == version) Drawing.BeginAnimation(OpacityProperty, null); };
            Drawing.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
        public void Finish() { version++; Drawing.BeginAnimation(OpacityProperty, null); }
    }
}

internal static class InstrumentIcons
{
    internal static string Name(InstrumentKind kind) => kind switch
    { InstrumentKind.Drums => "鼓", InstrumentKind.Bass => "贝斯", InstrumentKind.Guitar => "吉他", InstrumentKind.Piano => "键盘", InstrumentKind.Strings => "弦乐", InstrumentKind.Brass => "铜管", InstrumentKind.Woodwind => "木管", _ => "合成器" };
    internal static Geometry For(InstrumentKind kind)
    {
        string path = kind switch
        {
            InstrumentKind.Drums => "M3,11 C3,7 21,7 21,11 L21,19 C21,23 3,23 3,19 Z M3,11 C3,15 21,15 21,11 M5,1 L16,9 M20,1 L9,9 M7,14 L7,21 M17,14 L17,21",
            InstrumentKind.Bass => "M15,2 L21,2 L19,7 L16,7 L11,14 C16,16 13,23 7,22 C1,21 2,15 7,14 L14,5 Z M5,18 L10,18 M16,3 L7,19 M18,3 L9,19",
            InstrumentKind.Guitar => "M15,1 L20,3 L19,7 L16,7 L12,13 C17,17 12,24 6,22 C0,20 2,13 7,13 L14,6 Z M7,17 A2,2 0 1 0 11,17 A2,2 0 1 0 7,17 M17,4 L9,17",
            InstrumentKind.Piano => "M2,5 L22,5 L22,20 L2,20 Z M7,5 L7,20 M12,5 L12,20 M17,5 L17,20 M5,5 L5,13 M10,5 L10,13 M15,5 L15,13 M20,5 L20,13",
            InstrumentKind.Strings => "M10,1 L14,1 L14,7 C19,7 19,11 16,13 C20,18 17,23 12,23 C7,23 4,18 8,13 C5,11 5,7 10,7 Z M12,2 L12,21 M8,17 L16,17 M22,2 L19,22",
            InstrumentKind.Brass => "M2,9 L7,9 L7,6 L17,6 L23,2 L23,20 L17,16 L10,16 C6,16 6,11 10,11 L17,11 M11,3 L11,9 M15,3 L15,9 M3,7 L3,12",
            InstrumentKind.Woodwind => "M9,1 L14,1 L14,20 C14,23 8,23 8,20 L8,6 Z M8,7 L16,7 M8,12 L16,12 M8,17 L16,17 M19,6 L19,19 M8,3 L14,3",
            _ => "M2,8 L22,8 L22,22 L2,22 Z M7,13 L7,22 M12,13 L12,22 M17,13 L17,22 M5,13 L5,18 M10,13 L10,18 M15,13 L15,18 M4,3 A1,1 0 1 0 6,3 A1,1 0 1 0 4,3 M11,3 L15,3 M18,3 L22,3"
        };
        var geometry = Geometry.Parse(path); geometry.Freeze(); return geometry;
    }
}
