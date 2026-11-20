namespace Synchro.Core.Sync;

public sealed record FileDelta
{
    public required string Path { get; init; }

    public required FileDeltaKind Kind { get; init; }

    public string? MasterHash { get; init; }

    public string? SlaveHash { get; init; }
}
