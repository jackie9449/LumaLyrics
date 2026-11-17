namespace LumaLyrics.Core;

public static class ChineseScriptVisibility
{
    public static bool ShouldShow(LyricsDocument? document,bool mainTranslation,bool floatingTranslation)
    {
        if(mainTranslation || floatingTranslation)return true;
        if(document is null || document.Instrumental)return false;
        // Static imports have no timed groups. Inspect actual lyric lines in both
        // forms, excluding metadata/credits independently within multiline groups.
        var lines=document.IsSynced?document.Groups.SelectMany(g=>g.Lines):document.Raw.TrimStart('\uFEFF').Replace("\r","").Split('\n').AsEnumerable();
        string text=string.Join('\n',lines.Where(line=>TranslationAlignment.IsLyricText(line) && !(line.Trim().StartsWith('[') && line.Trim().EndsWith(']'))));
        if(text.Any(c=>char.IsLetter(c) && c is >= '\u3040' and <= '\u30ff' or >= '\u31f0' and <= '\u31ff' or >= '\uff66' and <= '\uff9f' or >= '\u1100' and <= '\u11ff' or >= '\u3130' and <= '\u318f' or >= '\uac00' and <= '\ud7af'))return false;
        return TranslationAlignment.ContainsHan(text);
    }
}
