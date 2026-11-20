using Synchro.Core.Store;

namespace Synchro.Core.Slave;

public abstract record AddSlaveResult
{
    public sealed record Registered(StoreState State, SlaveConfig Config, bool RepoBootstrapped) : AddSlaveResult;

    public sealed record ConsentDeclined : AddSlaveResult;

    public sealed record Refused(SyncError Error) : AddSlaveResult;
}
