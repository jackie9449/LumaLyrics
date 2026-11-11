namespace LumaLyrics.App;

internal static class UiTypography
{
    internal static string Sans=>UiText.Language switch
    {
        "ja"=>"Segoe UI Variable, Yu Gothic UI, Meiryo",
        "zh-Hant"=>"Segoe UI Variable, Microsoft JhengHei UI",
        "en"=>"Segoe UI Variable, Yu Gothic UI, Microsoft JhengHei UI, Microsoft YaHei UI",
        _=>"Segoe UI Variable, Microsoft YaHei UI"
    };
    internal static string Display(string original)=>UiText.Language switch
    {
        "ja"=>original.Replace("Microsoft YaHei UI","Yu Gothic UI, Meiryo").Replace("SimSun","Yu Mincho"),
        "zh-Hant"=>original.Replace("Microsoft YaHei UI","Microsoft JhengHei UI").Replace("SimSun","PMingLiU"),
        _=>original
    };
}
