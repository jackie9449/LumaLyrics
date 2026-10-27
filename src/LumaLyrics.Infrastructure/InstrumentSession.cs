using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

public sealed class InstrumentSession(InstrumentEngine engine, Func<InstrumentEpoch?> readCurrent, Action<InstrumentFrame> accept)
{
    public async Task SubmitAsync(InstrumentWindow window, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Equals(readCurrent(), window.Epoch)) return;
        var frame = await engine.AnalyzeAsync(window, token);
        token.ThrowIfCancellationRequested();
        if (!Equals(readCurrent(), frame.Epoch)) return;
        accept(frame);
    }
}
