namespace Synchro.Core.Landing;

public sealed record RollbackReport
{
    public sealed record SkippedRemoval(string Path, string Reason);

    public required bool RolledBack { get; init; }
    public required string Branch { get; init; }
    public required IReadOnlyList<string> RestoredPaths { get; init; }
    public required IReadOnlyList<string> RemovedPaths { get; init; }
    public IReadOnlyList<SkippedRemoval> Skipped { get; init; } = [];
}
