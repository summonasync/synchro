namespace Synchro.Core.Sync;

public sealed record SyncCommit
{
    public required string Sha { get; init; }

    public required string? PatchId { get; init; }

    public required IReadOnlyList<string> Files { get; init; }
}
