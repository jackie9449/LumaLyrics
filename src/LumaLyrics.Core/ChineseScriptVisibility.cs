namespace LumaLyrics.Core;

public static class ChineseScriptVisibility
{
    public static bool ShouldShow(LyricsDocument? document,bool mainTranslation,bool floatingTranslation)
    {
        if(mainTranslation || floatingTranslation)return true;
        if(document is null || document.Instrumental)return false;
        string text=string.Join('\n',document.Groups.Where(g=>TranslationAlignment.IsLyricText(g.Text)).Select(g=>g.Text));
        if(text.Any(c=>char.IsLetter(c) && c is >= '\u3040' and <= '\u30ff' or >= '\u31f0' and <= '\u31ff' or >= '\uff66' and <= '\uff9f' or >= '\u1100' and <= '\u11ff' or >= '\u3130' and <= '\u318f' or >= '\uac00' and <= '\ud7af'))return false;
        return TranslationAlignment.ContainsHan(text);
    }
}
