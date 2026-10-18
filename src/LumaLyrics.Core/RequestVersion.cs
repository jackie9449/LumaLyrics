namespace LumaLyrics.Core;
/// <summary>Cancellation alone is insufficient: a provider can finish after cancellation.</summary>
public sealed class RequestVersion
{
    long value;
    public long Next()=>Interlocked.Increment(ref value);
    public bool IsCurrent(long request)=>request==Interlocked.Read(ref value);
}
