namespace Synchro.Core.Sync;

public sealed record InboxState
{
    public required string Branch { get; init; }

    public required bool OnSyncBranch { get; init; }

    public required int DirtyFiles { get; init; }
}
