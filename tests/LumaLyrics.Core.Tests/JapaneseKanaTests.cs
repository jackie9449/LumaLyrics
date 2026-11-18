using LumaLyrics.Core;

namespace LumaLyrics.Core.Tests;

public sealed class JapaneseKanaTests
{
    static readonly Track Roman=new("Yoru no Uta","Hikari","Shizuka na Asa - Single",180000);
    static readonly Track Kana=new("ヨルノウタ","ヒカリ","シズカナアサ",179650);
    const string Original="[00:01]ひかりが窓にゆれる\n[00:50]夜の道を歩く\n[01:55]静かな朝が来る";
    static LyricsCandidate Candidate(Track track,long id=1)=>new(id,track,Original,null,false);

    [Theory]
    [InlineData("Yoru no Uta","ﾖﾙﾉｳﾀ")]
    [InlineData("Tōkyō","トーキョー")]
    [InlineData("Toukyou","トーキョー")]
    [InlineData("Tokyo","とうきょう")]
    [InlineData("Maccha","マッチャ")]
    [InlineData("Matcha","まっちゃ")]
    [InlineData("Shin’Ya","シンヤ")]
    [InlineData("Shiroi Kippu","しろいキップ")]
    [InlineData("Sizuka","シズカ")]
    [InlineData("Fāsutō","ファーストー")]
    [InlineData("Sampō","サンポウ")]
    [InlineData("しずか","シズカ")]
    public void CommonReadingVariantsCompareSymmetrically(string roman,string kana)
    {
        Assert.True(JapaneseKana.SameReading(roman,kana));
        Assert.True(JapaneseKana.SameReading(kana,roman));
    }

    [Theory]
    [InlineData("Yoru no Uta","ヨルノウタ")]
    [InlineData("Fāto","ファート")]
    [InlineData("Matcha","マッチャ")]
    [InlineData("Shin’Ya","シンヤ")]
    [InlineData("Weather Report",null)]
    [InlineData("東京",null)]
    public void SearchSuggestionsRequireACompleteSupportedSpelling(string text,string? expected)
        =>Assert.Equal(expected,JapaneseKana.SearchKatakana(text));

    [Fact]
    public void MatchedTitleArtistAndAlbumCanUseDifferentScriptsWithoutChangingTrackIdentity()
    {
        var match=MatchRules.Score(Roman,Candidate(Kana));
        Assert.True(match.Eligible,match.Reason);
        Assert.True(match.UsesJapaneseReading);
        Assert.Equal(1,match.Score,8);
        Assert.NotEqual(Roman.Fingerprint,Kana.Fingerprint);
        Assert.NotNull(AutomaticLyricsMatcher.Select(Roman,[Candidate(Kana)]));
        var literal=Candidate(Roman,2);
        Assert.Same(literal,AutomaticLyricsMatcher.Select(Roman,[Candidate(Kana),literal])!.Candidate);
    }

    [Fact]
    public void ReadingMatchCannotReplaceEditionDurationArtistOrTimingChecks()
    {
        var unrelated=new[]{Kana with{Album="ベツノアルバム"},Kana with{Album=""},Kana with{Album=Kana.Album+" (ライブ)"},
            Kana with{Artist="ヒカル"},Kana with{DurationMs=177900},Kana with{DurationMs=null}};
        foreach(var item in unrelated)Assert.Null(AutomaticLyricsMatcher.Select(Roman,[Candidate(item)]));
        Assert.Null(AutomaticLyricsMatcher.Select(Roman,[Candidate(Kana) with{SyncedLyrics="[99:00]時間が違う"}]));
        Assert.False(JapaneseKana.SameReading("Tokyo","東京"));
        Assert.False(JapaneseKana.SameReading("Hikari","Hikaru"));
    }

    [Fact]
    public void MixedArtistCreditsCanBeReorderedWithIndependentReadingEvidence()
    {
        var local=Roman with{Artist="Hikari & Aimer"};
        Assert.True(MatchRules.Score(local,Candidate(Kana with{Artist="Aimer & ヒカリ"})).Eligible);
        Assert.False(MatchRules.Score(local,Candidate(Kana with{Artist="Someone else & ヒカリ"})).Eligible);
    }
}
