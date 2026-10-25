using LumaLyrics.Infrastructure;

namespace LumaLyrics.Infrastructure.Tests;

public class PassiveReadCircuitTests
{
    [Fact]
    public void TimedOutRpcKeepsOwnershipUntilItReallyReturns()
    {
        var circuit=new PassiveReadCircuit();var original=circuit.TryEnter("17:100")!;
        original.Quarantine();
        Assert.Null(circuit.TryEnter("17:100"));
        Assert.Null(circuit.TryEnter("28:200"));
        original.Dispose();
        Assert.Null(circuit.TryEnter("17:100"));
        using var restarted=circuit.TryEnter("17:300");
        Assert.NotNull(restarted); // PID reuse must not keep the previous quarantine.
    }

    [Fact]
    public void NormalCompletionReleasesSingleFlightWithoutQuarantine()
    {
        var circuit=new PassiveReadCircuit();
        using(var first=circuit.TryEnter("17:100")){Assert.NotNull(first);Assert.Null(circuit.TryEnter("17:100"));}
        using var second=circuit.TryEnter("17:100");Assert.NotNull(second);
    }

    [Fact]
    public void StaleCleanupCannotReleaseOrQuarantineANewerLease()
    {
        var circuit=new PassiveReadCircuit();var old=circuit.TryEnter("17:100")!;old.Dispose();
        var current=circuit.TryEnter("28:200")!;old.Quarantine();old.Dispose();
        Assert.Null(circuit.TryEnter("28:200"));current.Dispose();
        using var next=circuit.TryEnter("28:200");Assert.NotNull(next);
    }
}
