using OpenccNetLib;
using System.Text.Json;
using ZstdSharp;

namespace LumaLyrics.Windows;

public static class ChineseScriptConverter
{
    // The package embeds its dictionary; no files, downloads or platform DLLs are needed.
    static readonly Lazy<Opencc> Converter = new(CreateConverter);

    static Opencc CreateConverter()
    {
        try { return new(OpenccConfig.S2T, isFrozen: true); }
        catch (Exception error) when (error is IOException or JsonException or ZstdException or InvalidOperationException)
        {
            throw new InvalidOperationException("内置中文简繁转换词典无法加载。", error);
        }
    }

    public static string Convert(string text, string mode)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return text;
        // The direct methods propagate failures; Opencc.Convert returns error text instead.
        // Keep punctuation and regional vocabulary intact: this changes Chinese script only.
        return mode switch
        {
            "simplified" => Converter.Value.T2S(text, punctuation: false),
            "traditional" => Converter.Value.S2T(text, punctuation: false),
            _ => text
        };
    }
}
