namespace LumaLyrics.Infrastructure;

public static class OverlayContrast
{
    // Separate entry/exit thresholds prevent flicker on medium-bright backgrounds.
    public static bool UseDarkText(double brightness,bool wasDark)
        =>double.IsFinite(brightness)?brightness>(wasDark?.50:.64):wasDark;
    public static OverlayAppearance Paint(OverlayAppearance appearance,bool dark)
        =>appearance.AdaptiveContrast && appearance.Material=="liquid" && !appearance.Transparent
            ?appearance with{Color=dark?"#202832":"#F7F9FC",Shadow=!dark && appearance.Shadow}
            :appearance;
}
