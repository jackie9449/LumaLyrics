namespace LumaLyrics.Infrastructure;

// Cancellation of a caller does not cancel an outstanding native RPC. Its lease
// therefore belongs to the worker, until that worker has actually returned.
public sealed class PassiveReadCircuit
{
    readonly object sync=new();
    Lease? active;
    string? quarantined;

    public Lease? TryEnter(string processLifetime)
    {
        lock(sync)
        {
            if(active is not null || quarantined==processLifetime)return null;
            return active=new Lease(this,processLifetime);
        }
    }

    public sealed class Lease : IDisposable
    {
        readonly PassiveReadCircuit owner;
        readonly string identity;
        internal Lease(PassiveReadCircuit owner,string identity){this.owner=owner;this.identity=identity;}
        public void Quarantine()
        {
            lock(owner.sync)if(owner.active==this)owner.quarantined=identity;
        }
        public void Dispose()
        {
            lock(owner.sync)if(owner.active==this)owner.active=null;
        }
    }
}
