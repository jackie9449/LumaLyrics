using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

public sealed record InstrumentModelOption(string Id, string Name, string Scope, long Bytes, string ExportFolder, string ExportFile, InstrumentInferenceProfile Profile)
{
    public override string ToString() => Name;
}

/// <summary>Finite instrument vocabulary experiment. General accuracy is still unvalidated.</summary>
public static class InstrumentModelCatalog
{
    public const string StringsId = "yamnet-chamber-strings-experimental-v1";
    public const string RockId = "mtg-rock-three-experimental-v1";
    public const string GeneralId = "matpac-song-instruments-experimental-v1";
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
    public static InstrumentInferenceProfile GeneralExperimental
    {
        get
        {
            var labels = new Dictionary<int, InstrumentKind> { [0] = InstrumentKind.Accordion, [1] = InstrumentKind.Banjo,
                [2] = InstrumentKind.Bass, [3] = InstrumentKind.Cello, [4] = InstrumentKind.Clarinet, [5] = InstrumentKind.Cymbals,
                [6] = InstrumentKind.Drums, [7] = InstrumentKind.Flute, [8] = InstrumentKind.Guitar, [9] = InstrumentKind.MalletPercussion,
                [10] = InstrumentKind.Mandolin, [11] = InstrumentKind.Organ, [12] = InstrumentKind.Piano, [13] = InstrumentKind.Saxophone,
                [14] = InstrumentKind.Synth, [15] = InstrumentKind.Trombone, [16] = InstrumentKind.Trumpet,
                [17] = InstrumentKind.Ukulele, [18] = InstrumentKind.Violin };
            return new()
            {
                Id = GeneralId, Sha256 = "eb7f7460012bfbf93f029404542faa2e57ffd6ebb4050b18d7bdd6e6b301119f",
                SampleRate = 16000, WindowSamples = 48880, HopSamples = 8000,
                InputName = "waveform", OutputName = "scores", OutputClassCount = 20,
                PreprocessingVersion = "matpac-304-slaney-power-ln-f64dft-layer10-v1", LabelMap = labels,
                Thresholds = labels.Values.ToDictionary(kind => kind, kind => kind is InstrumentKind.Guitar or InstrumentKind.Bass or InstrumentKind.Drums or InstrumentKind.Cymbals
                    ? new ActivityParameters(.5f, .3f, 2, 750) : new ActivityParameters(.7f, .45f, 3, 750))
            };
        }
    }
    public static InstrumentModelOption Get(string? id) => id switch
    {
        StringsId => new(StringsId, "室内乐弦乐兼容模型（实验）", Scope, ModelBytes, "yamnet-fft-export", "yamnet-waveform.onnx", Experimental),
        RockId => new(RockId, "旧版摇滚三类模型（实验）", "旧版三类 · 吉他／贝斯／鼓", 20374087, "mtg-export-official", "mtg-jamendo-waveform.onnx", RockExperimental),
        _ => new(GeneralId, "通用多乐器：按歌曲识别（实验）", "随当前歌曲生成清单 · 尝试识别 19 类乐器 · 准确度与进出时刻仍未验收", 322477291, "matpac-export", "matpac-openmic-waveform.onnx", GeneralExperimental)
    };
    public static IReadOnlyList<InstrumentModelOption> All => [Get(GeneralId), Get(StringsId)];
}
