using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

public sealed record InstrumentModelOption(string Id, string Name, string Scope, long Bytes, string ExportFolder, string ExportFile, InstrumentInferenceProfile Profile)
{
    public override string ToString() => Name;
}

/// <summary>Restricted experiment. General eight-family accuracy gate is still unpassed.</summary>
public static class InstrumentModelCatalog
{
    public const string StringsId = "yamnet-chamber-strings-experimental-v1";
    public const string RockId = "mtg-rock-three-experimental-v1";
    public const long ModelBytes = 16064581;
    public const string ModelSha256 = "5e08c523631c1bcc62489b724024e0d55e93da130c68cc650e1fc52a486ffb1a";
    public const string Scope = "实验识别 · 当前仅启用古典室内乐中的弦乐组";
    public static InstrumentInferenceProfile Experimental => new()
    {
        Id = StringsId, Sha256 = ModelSha256,
        SampleRate = 16000, WindowSamples = 15600, HopSamples = 7680,
        InputName = "waveform", OutputName = "Identity:0", OutputClassCount = 521,
        PreprocessingVersion = "official-yamnet-fft-waveform-1",
        LabelMap = new() { [185] = InstrumentKind.Strings, [186] = InstrumentKind.Strings, [188] = InstrumentKind.Strings },
        Thresholds = new() { [InstrumentKind.Strings] = new(.1f, .065f, 1, 500) }
    };
    public static InstrumentInferenceProfile RockExperimental => new()
    {
        Id = RockId, Sha256 = "dbbd8c9371dc11d77ec40e82dedcbcffe922ebce618ea7973342c393d9f73a35",
        SampleRate = 16000, WindowSamples = 33024, HopSamples = 8000,
        InputName = "waveform", OutputName = "scores", OutputClassCount = 40,
        PreprocessingVersion = "musicnn-interior-slaney-power-log10-v1",
        LabelMap = new() { [15] = InstrumentKind.Guitar, [18] = InstrumentKind.Guitar, [2] = InstrumentKind.Guitar,
            [3] = InstrumentKind.Bass, [14] = InstrumentKind.Drums },
        Thresholds = new() { [InstrumentKind.Guitar] = new(.5f, .3f, 2, 750), [InstrumentKind.Bass] = new(.5f, .3f, 2, 750), [InstrumentKind.Drums] = new(.5f, .3f, 2, 750) }
    };
    public static InstrumentModelOption Get(string? id) => id == RockId
        ? new(RockId, "摇滚／金属：吉他、贝斯、鼓（实验）", "摇滚三类实验 · 吉他／贝斯／鼓 · 通用准确度与边界未验收", 20374087, "mtg-export-official", "mtg-jamendo-waveform.onnx", RockExperimental)
        : new(StringsId, "古典室内乐：弦乐组（实验）", Scope, ModelBytes, "yamnet-fft-export", "yamnet-waveform.onnx", Experimental);
    public static IReadOnlyList<InstrumentModelOption> All => [Get(StringsId), Get(RockId)];
}
