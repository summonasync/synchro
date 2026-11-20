namespace Synchro.Core.Sync;

public sealed record CommitDisposition
{
    public required SyncCommit Commit { get; init; }

    public required OfferDisposition Disposition { get; init; }
}
