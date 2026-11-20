namespace Synchro.Core.Install;

public sealed record InstallReport
{
    public required bool UpToDate { get; init; }
    public required string? PreviousVersion { get; init; }
    public required string Version { get; init; }
    public required string BinaryPath { get; init; }
    public required bool NameRegistered { get; init; }
    public required bool PathReady { get; init; }
    public required bool UsedLockedTargetFallback { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}
