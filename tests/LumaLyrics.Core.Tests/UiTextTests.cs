using LumaLyrics.Core;
namespace LumaLyrics.Core.Tests;

[CollectionDefinition("Application localization",DisableParallelization=true)]
public sealed class ApplicationLocalizationCollection;

[Collection("Application localization")]
public sealed class UiTextTests : IDisposable
{
    readonly string previous=UiText.Language;
    [Fact]
    public void NestedCanonicalErrorsLocalizeWithoutChangingAFilePath()
    {
        UiText.SetLanguage("en");
        Assert.Equal("Library scan failed: A personal file is missing or failed verification: D:\\音乐\\设置.lrc",UiText.Status("资料库扫描失败：个人文件缺失或校验不一致：D:\\音乐\\设置.lrc"));
    }
    public void Dispose()=>UiText.SetLanguage(previous);

    [Fact]
    public void UnknownLanguageUsesExistingSimplifiedChineseDefault()
    {
        UiText.SetLanguage("unknown");
        Assert.Equal("zh-Hans",UiText.Language);
        Assert.Equal("接下来还没有歌曲",UiText.T("接下来还没有歌曲"));
        Assert.Equal("zh-Hans",UiText.Normalize(null));
    }

    [Theory]
    [InlineData("en","No songs queued next")]
    [InlineData("ja","次に再生する曲はありません")]
    public void FixedMessagesUseTheSelectedCatalog(string language,string expected)
    {
        UiText.SetLanguage(language);
        Assert.Equal(expected,UiText.T("接下来还没有歌曲"));
    }

    [Theory]
    [InlineData("en")][InlineData("ja")]
    public void FormattedMessagesPreserveTheirLiteralArguments(string language)
    {
        UiText.SetLanguage(language);
        const string metadata="纯音乐 · 原創歌名 🌙 {0} C:\\个人歌词\\a.lrc";
        string result=UiText.F($"准备中止：{metadata}");
        Assert.Contains(metadata,result,StringComparison.Ordinal);
        Assert.DoesNotContain("准备中止：",result,StringComparison.Ordinal);
        Assert.Equal("",UiText.T(""));
    }

    [Fact]
    public void NumericFormatsRetainTheirPlaceholderAndRequestedFormat()
    {
        UiText.SetLanguage("en");int count=12345;
        Assert.Equal("Read 12,345 songs; Apple Music has not provided the remaining details yet.",UiText.F($"已读取 {count:N0} 首；Apple Music 暂未完整提供其余信息。"));
    }

    [Fact]
    public void CanonicalProgressLocalizesEveryLineWithoutChangingArguments()
    {
        UiText.SetLanguage("en");
        const string original="资料库 10 首 · 动态歌词 4 · 纯音乐 1 · 手动保留 2\n待处理 3 · 待补齐 2 · 网络待重试 1\n已暂停，下次从本地记录继续";
        Assert.Equal("Library: 10 songs · Timed lyrics: 4 · Instrumentals: 1 · Manual: 2\nPending: 3 · Missing: 2 · Network retries: 1\nPaused. The next run will resume from local records.",UiText.Status(original));
        const string argument="纯音乐 · 用户原始文字 🌙";
        Assert.Equal("Preparation stopped: "+argument,UiText.Status("准备中止："+argument));
    }

    [Fact]
    public void SearchResultsWithUnavailableSourcesLocalizeAsACompleteMessage()
    {
        UiText.SetLanguage("en");
        Assert.Equal("Found 3 candidates: timed 2 · untimed 1 · instrumental 0. Choose the recording. Some sources are unavailable: 原始来源 🌙. Try again later.",UiText.Status("找到 3 个候选：同步 2 · 静态 1 · 纯音乐 0。请选择录音版本。 部分来源暂不可用：原始来源 🌙。可稍后重试。"));
        Assert.Equal("No candidates. Try only the song title or a shorter artist name. Some sources are unavailable: 原始来源. Try again later.",UiText.Status("未找到候选，可只填歌名或简化歌手名重试。 部分来源暂不可用：原始来源。可稍后重试。"));
    }

    [Fact]
    public void UnknownContentIsNeverTranslatedOrStripped()
    {
        UiText.SetLanguage("en");
        const string unknown="原创标题と原文 🌙\nC:\\个人资料\\歌词.lrc\r\n";
        Assert.Equal(unknown,UiText.Status(unknown));
        Assert.Equal(unknown,UiText.T(unknown));
    }

    [Fact]
    public void SwitchingLanguagesDoesNotReuseThePreviousStatusCache()
    {
        UiText.SetLanguage("en");Assert.Equal("Chinese complete 3/4",UiText.Status("中文完整 3/4"));
        UiText.SetLanguage("ja");Assert.Equal("中国語訳あり 3/4",UiText.Status("中文完整 3/4"));
        UiText.SetLanguage("zh-Hans");Assert.Equal("中文完整 3/4",UiText.Status("中文完整 3/4"));
    }

    [Fact]
    public void AllTranslatedCatalogsPreserveCompositePlaceholderIndexes()
    {
        foreach(string language in new[]{"zh-Hant","en","ja"})
        {
            Assert.NotEmpty(UiText.Catalogs[language]);
            foreach(var pair in UiText.Catalogs[language])
                Assert.Equal(UiText.Placeholders(pair.Key),UiText.Placeholders(pair.Value));
        }
    }
}
