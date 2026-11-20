using Synchro.Core.Store;

namespace Synchro.Core.Sync;

public sealed record SyncRunOutput
{
    public required SyncRunReport Report { get; init; }

    public required StoreState State { get; init; }
}
