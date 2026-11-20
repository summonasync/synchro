namespace Synchro.Core.Install;

public sealed record UninstallReport
{
    public required bool NothingInstalled { get; init; }
    public required string? RemovedVersion { get; init; }
    public required bool BinaryRemoved { get; init; }
    public required bool NameRemoved { get; init; }
    public required bool PathEntryRemoved { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}
