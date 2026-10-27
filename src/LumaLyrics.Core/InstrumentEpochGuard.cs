namespace LumaLyrics.Core;

public sealed class InstrumentEpochGuard
{
    InstrumentEpoch? current;
    public void Set(InstrumentEpoch value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Volatile.Write(ref current, value);
    }
    public void Clear() => Volatile.Write(ref current, null);
    public bool IsCurrent(InstrumentEpoch value) => Equals(Volatile.Read(ref current), value);
}
