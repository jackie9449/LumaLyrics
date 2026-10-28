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
    public const string LegacyGeneralId = "matpac-song-instruments-experimental-v1";
    public const string GeneralId = "matpac-song-instruments-responsive-v2";
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
    public static InstrumentInferenceProfile LegacyGeneralExperimental
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
                Id = LegacyGeneralId, Sha256 = "eb7f7460012bfbf93f029404542faa2e57ffd6ebb4050b18d7bdd6e6b301119f",
                SampleRate = 16000, WindowSamples = 48880, HopSamples = 8000,
                InputName = "waveform", OutputName = "scores", OutputClassCount = 20,
                PreprocessingVersion = "matpac-304-slaney-power-ln-f64dft-layer10-v1", LabelMap = labels,
                Thresholds = labels.Values.ToDictionary(kind => kind, kind => kind is InstrumentKind.Guitar or InstrumentKind.Bass or InstrumentKind.Drums or InstrumentKind.Cymbals
                    ? new ActivityParameters(.5f, .3f, 2, 750) : new ActivityParameters(.7f, .45f, 3, 750))
            };
        }
    }
    public static InstrumentInferenceProfile GeneralExperimental
    {
        get
        {
            var basis = LegacyGeneralExperimental;
            return basis with
            {
                Id = GeneralId, Sha256 = "11a7162cf36efe2bd81506d8bddf8e9f96e60583c9d5f2f5f691ccd0ab214a8e",
                WindowSamples = 23280, HopSamples = 3200, RecentSilenceSamples = 1280,
                PreprocessingVersion = "matpac-144-slaney-power-ln-f64dft-layer10-gate-v2",
                Thresholds = basis.Thresholds.Keys.ToDictionary(kind => kind, kind => kind is InstrumentKind.Guitar or InstrumentKind.Bass or InstrumentKind.Drums or InstrumentKind.Cymbals
                    ? new ActivityParameters(.5f, .4f, 1, 200) : new ActivityParameters(.7f, .6f, 1, 200)),
                RosterThresholds = basis.Thresholds.Keys.ToDictionary(kind => kind, kind => kind switch
                {
                    InstrumentKind.Bass => new ActivityParameters(.6f, .5f, 4, 0),
                    InstrumentKind.Guitar => new ActivityParameters(.9f, .8f, 4, 0),
                    InstrumentKind.Drums or InstrumentKind.Cymbals => new ActivityParameters(.85f, .75f, 4, 0),
                    InstrumentKind.Piano or InstrumentKind.Synth => new ActivityParameters(.95f, .85f, 5, 0),
                    _ => new ActivityParameters(.9f, .8f, 5, 0)
                })
            };
        }
    }
    public static InstrumentModelOption Get(string? id) => id switch
    {
        StringsId => new(StringsId, "室内乐弦乐兼容模型（实验）", Scope, ModelBytes, "yamnet-fft-export", "yamnet-waveform.onnx", Experimental),
        RockId => new(RockId, "旧版摇滚三类模型（实验）", "旧版三类 · 吉他／贝斯／鼓", 20374087, "mtg-export-official", "mtg-jamendo-waveform.onnx", RockExperimental),
        LegacyGeneralId => new(LegacyGeneralId, "旧版通用多乐器模型（实验）", "旧版 3.06 秒上下文 · 仅供复现", 322477291, "matpac-export", "matpac-openmic-waveform.onnx", LegacyGeneralExperimental),
        _ => new(GeneralId, "通用多乐器：快速响应（实验）", "按歌曲识别 19 类 · 更快亮灭与严格清单确认 · 仍可能误报或漏报", 322341960, "matpac-responsive-export", "matpac-responsive-waveform.onnx", GeneralExperimental)
    };
    public static IReadOnlyList<InstrumentModelOption> All => [Get(GeneralId), Get(StringsId)];
}
