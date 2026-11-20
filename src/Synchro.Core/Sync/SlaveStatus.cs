using Synchro.Core.Store;

namespace Synchro.Core.Sync;

public sealed record SlaveStatus
{
    public required SlaveConfig Config { get; init; }

    public required LedgerHint? Hint { get; init; }

    public required InboxState? SlaveInbox { get; init; }

    public required InboxState? MasterInbox { get; init; }

    public required bool InFlight { get; init; }

    public required SyncHistoryRecord? LastOperation { get; init; }
}
