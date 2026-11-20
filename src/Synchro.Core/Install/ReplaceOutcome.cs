namespace Synchro.Core.Install;

public sealed record ReplaceOutcome
{
    public required bool UsedLockedTargetFallback { get; init; }
    public required bool OldCleanupPending { get; init; }
}
