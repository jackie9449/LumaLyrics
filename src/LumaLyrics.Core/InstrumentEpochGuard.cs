namespace LumaLyrics.Core;

public sealed class InstrumentEpochGuard
{
    InstrumentEpoch? current;
    public InstrumentEpoch? Current => Volatile.Read(ref current);
    public void Set(InstrumentEpoch value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Volatile.Write(ref current, value);
    }
    public void Clear() => Volatile.Write(ref current, null);
    public bool IsCurrent(InstrumentEpoch value) => Equals(Volatile.Read(ref current), value);
    public InstrumentEpoch? AdvanceDiscontinuity(InstrumentEpoch expected)
    {
        var observed = Volatile.Read(ref current);
        if (!Equals(observed, expected)) return null;
        var next = expected with { Discontinuity = checked(expected.Discontinuity + 1) };
        return ReferenceEquals(Interlocked.CompareExchange(ref current, next, observed), observed) ? next : null;
    }
}
