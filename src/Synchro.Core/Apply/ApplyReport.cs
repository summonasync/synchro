using Synchro.Core.Store;

namespace Synchro.Core.Apply;

public sealed record ApplyReport
{
    public required IReadOnlyList<string> AppliedFiles { get; init; }
    public required IReadOnlyList<string> AppliedDeletions { get; init; }
    public required IReadOnlyList<string> ConflictPaths { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool AlreadyApplied { get; init; }
    public required bool ScenePending { get; init; }
    public required StoreState? UpdatedState { get; init; }
}
