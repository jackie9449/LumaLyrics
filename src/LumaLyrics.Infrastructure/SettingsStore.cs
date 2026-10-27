using System.Text;
using System.Text.Json;
namespace LumaLyrics.Infrastructure;

public sealed class AppSettings
{
    public string UiTheme { get; set; } = AppThemes.Default;
    public bool ShowPlaybackQueue { get; set; } = true;
    public bool ShowInstrumentActivity { get; set; }
    public bool AnalyzeInstrumentsInBackground { get; set; }
    public string InstrumentModelId { get; set; } = InstrumentModelCatalog.GeneralId;
    public bool OnlineLookup { get; set; } = true;
    public bool AppleMusicOriginalLyrics {get;set;}=true;
    public bool AutoPrefetchLibrary { get; set; } = true;
    public bool ShowChineseTranslation { get; set; }
    public bool OverlayShowChineseTranslation { get; set; }
    public string? LibraryPath { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool ReduceMotion { get; set; }
    public double GlobalOffsetMs { get; set; }
    public double OverlayFontSize { get; set; } = 32;
    public string OverlayColor { get; set; } = "#F5F7FA";
    public string OverlayFont { get; set; } = "Microsoft YaHei UI";
    public double OverlayBackgroundOpacity { get; set; } = .65;
    public string OverlayPreset { get; set; } = "柔和卡片";
    public Dictionary<string, OverlayAppearance> OverlayThemeProfiles { get; set; } = [];
    public bool OverlayTwoLines { get; set; } = true;
    public bool OverlayShadow { get; set; } = true;
    public bool OverlayAdaptiveContrast { get; set; }
    public bool OverlayAnimations { get; set; } = true;
    public double OverlayTransitionMs { get; set; } = 220;
    public string OverlayMaterial { get; set; } = "card";
    public string OverlayBackgroundColor { get; set; } = "#101216";
    public string OverlayAccentColor { get; set; } = "#B8A4FF";
    public string OverlayFontWeight { get; set; } = "SemiBold";
    public bool OverlayTransparent { get; set; }
    public bool OverlaySystemBlur { get; set; } = true;
    public bool OverlayBorder { get; set; } = true;
    public double OverlayCornerRadius { get; set; } = 24;
    public double? OverlayCornerRatio { get; set; }
    public double OverlayPadding { get; set; } = 24;
    public double OverlayLineGap { get; set; } = 8;
    public double OverlayNextScale { get; set; } = .65;
    public double OverlayNextOpacity { get; set; } = .65;
    public double OverlayShadowStrength { get; set; } = .8;
    public double OverlayGlassIntensity { get; set; } = .55;
    public string OverlayAlignment { get; set; } = "Center";
    public double OverlayWidth { get; set; } = 820;
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public double? MainLeft { get; set; }
    public double? MainTop { get; set; }
    public bool OverlayVisible { get; set; } = true;
    public bool FadeOnPause { get; set; }
    public string ToggleHotkey { get; set; } = "Ctrl+Alt+L";
    public string UnlockHotkey { get; set; } = "Ctrl+Alt+U";
    public string? PreferredSession { get; set; }
}
public sealed class SettingsStore
{
    public string Root { get; }
    readonly string path;
    public SettingsStore(string? root = null) { Root = DataDirectoryConfiguration.Resolve(root); PersonalLibraryArchive.EnsureOutsideSoftware(Root); Directory.CreateDirectory(Root); DataDirectoryConfiguration.ProtectFromGit(Root); path = Path.Combine(Root, "settings.json"); }
    public AppSettings Load()
    {
        try
        {
            var settings = File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new() : new();
            // The three-class product option is superseded by the per-song model.
            if (settings.InstrumentModelId == InstrumentModelCatalog.RockId) settings.InstrumentModelId = InstrumentModelCatalog.GeneralId;
            return settings;
        }
        catch (Exception e) when (e is JsonException or IOException) { if (File.Exists(path)) File.Copy(path, path + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}", true); return new(); }
    }
    public void Save(AppSettings settings)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
    public void Log(string message)
    {
        try { var dir = Path.Combine(Root, "logs"); Directory.CreateDirectory(dir); var file = Path.Combine(dir, $"app-{DateTime.UtcNow:yyyyMMdd}.log"); File.AppendAllText(file, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}"); foreach (var old in Directory.GetFiles(dir).OrderDescending().Skip(7)) File.Delete(old); } catch (IOException) { }
    }
}
