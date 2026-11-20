namespace Synchro.Core.Install;

public sealed record InstallRequest
{
    public required string SourcePath { get; init; }
    public required string Version { get; init; }
    public bool ApplyPathFix { get; init; }
    public bool ForceName { get; init; }
}
