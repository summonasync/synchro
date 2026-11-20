namespace Synchro.Core.Install;

public sealed record BinaryPlacement
{
    public required string TargetPath { get; init; }
    public required bool UsedLockedTargetFallback { get; init; }
    public bool OldCleanupPending { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}
