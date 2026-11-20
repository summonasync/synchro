using Synchro.Core.Landing;
using Synchro.Core.Store;

namespace Synchro.Core.Sync;

public sealed record SyncRunReport
{
    public required string SlaveName { get; init; }

    public required bool NonInteractive { get; init; }

    public required IReadOnlyList<SyncConflict> Conflicts { get; init; }

    public required IReadOnlyList<string> AlreadyCommitted { get; init; }

    public required LandingReport? ToSlave { get; init; }

    public required LandingReport? ToMaster { get; init; }
}
