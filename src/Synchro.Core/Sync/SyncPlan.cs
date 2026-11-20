namespace Synchro.Core.Sync;

public sealed record SyncPlan
{
    public required HistoryRelation Relation { get; init; }

    public required string? MergeBase { get; init; }

    public required string MasterHead { get; init; }

    public required string SlaveHead { get; init; }

    public required string? SlaveSyncHead { get; init; }

    public required bool DeSync { get; init; }

    public required bool NoOp { get; init; }

    public required IReadOnlyList<CommitDisposition> MasterOffers { get; init; }

    public required IReadOnlyList<SyncCommit> SlaveOffers { get; init; }

    public required IReadOnlyList<FileDelta> FileMap { get; init; }

    public required LedgerHint Hint { get; init; }
}
