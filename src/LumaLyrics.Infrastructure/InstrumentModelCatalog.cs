using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

/// <summary>Restricted experiment. General eight-family accuracy gate is still unpassed.</summary>
public static class InstrumentModelCatalog
{
    public const long ModelBytes = 16064581;
    public const string ModelSha256 = "5e08c523631c1bcc62489b724024e0d55e93da130c68cc650e1fc52a486ffb1a";
    public const string Scope = "实验识别 · 当前仅启用古典室内乐中的弦乐组";
    public static InstrumentInferenceProfile Experimental => new()
    {
        Id = "yamnet-chamber-strings-experimental-v1", Sha256 = ModelSha256,
        SampleRate = 16000, WindowSamples = 15600, HopSamples = 7680,
        InputName = "waveform", OutputName = "Identity:0", OutputClassCount = 521,
        PreprocessingVersion = "official-yamnet-fft-waveform-1",
        LabelMap = new() { [185] = InstrumentKind.Strings, [186] = InstrumentKind.Strings, [188] = InstrumentKind.Strings },
        Thresholds = new() { [InstrumentKind.Strings] = new(.1f, .065f, 1, 500) }
    };
}
