using System.Windows;

namespace LumaLyrics.App;

public partial class MainWindow
{
    void InstrumentDemandChanged(object sender, DependencyPropertyChangedEventArgs e) => RefreshInstrumentDemand();
    void InstrumentWindowStateChanged(object? sender, EventArgs e) => RefreshInstrumentDemand();
    void RefreshInstrumentDemand() => coordinator?.Instruments.SetDemand(IsVisible && WindowState != WindowState.Minimized && LyricsPage.IsVisible);
    void RefreshInstrumentView()
    {
        RefreshInstrumentDemand();
        coordinator.Instruments.SetSeeking(SeekSlider.IsEditing || seekPending);
        var state = coordinator.Instruments.View(SeekSlider.IsEditing || seekPending ? null : coordinator.Position);
        var visibility = settings.ShowInstrumentActivity ? Visibility.Visible : Visibility.Collapsed;
        bool changed = InstrumentHost.Visibility != visibility;
        InstrumentHost.Visibility = visibility; instrumentView.Apply(state);
        if (changed) ApplyPlaybackLayout();
    }
}
