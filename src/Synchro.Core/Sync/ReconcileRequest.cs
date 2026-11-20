namespace Synchro.Core.Sync;

public sealed record ReconcileRequest
{
    public required string MasterRoot { get; init; }

    public required string MasterBaseBranch { get; init; }

    public required string SlaveRoot { get; init; }

    public required string SlaveBaseBranch { get; init; }

    public required string SlaveSyncBranch { get; init; }
}
