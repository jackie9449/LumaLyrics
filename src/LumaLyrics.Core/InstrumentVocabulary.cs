namespace LumaLyrics.Core;

/// <summary>Display vocabulary and family fallbacks. Never supplies a song roster.</summary>
public static class InstrumentVocabulary
{
    public static string Name(InstrumentKind kind) => kind switch
    {
        InstrumentKind.Drums => "鼓", InstrumentKind.Bass => "贝斯", InstrumentKind.Guitar => "吉他",
        InstrumentKind.Piano => "钢琴", InstrumentKind.Strings => "弦乐", InstrumentKind.Brass => "铜管",
        InstrumentKind.Woodwind => "木管", InstrumentKind.Synth => "合成器", InstrumentKind.Accordion => "手风琴",
        InstrumentKind.Banjo => "班卓琴", InstrumentKind.Cello => "大提琴", InstrumentKind.Clarinet => "单簧管",
        InstrumentKind.Cymbals => "镲", InstrumentKind.Flute => "长笛", InstrumentKind.MalletPercussion => "键盘打击乐",
        InstrumentKind.Mandolin => "曼陀林", InstrumentKind.Organ => "风琴", InstrumentKind.Saxophone => "萨克斯",
        InstrumentKind.Trombone => "长号", InstrumentKind.Trumpet => "小号", InstrumentKind.Ukulele => "尤克里里",
        InstrumentKind.Violin => "小提琴", _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static InstrumentKind[] VisibleKinds(IEnumerable<InstrumentKind> kinds)
    {
        var known = kinds.ToHashSet();
        if (known.Any(kind => kind is InstrumentKind.Violin or InstrumentKind.Cello)) known.Remove(InstrumentKind.Strings);
        if (known.Any(kind => kind is InstrumentKind.Trumpet or InstrumentKind.Trombone)) known.Remove(InstrumentKind.Brass);
        if (known.Any(kind => kind is InstrumentKind.Flute or InstrumentKind.Clarinet or InstrumentKind.Saxophone)) known.Remove(InstrumentKind.Woodwind);
        return known.Order().ToArray();
    }
}
