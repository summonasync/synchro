namespace Synchro.Core.Store;

public sealed record ChannelConfig
{
    public required string Name { get; init; }
    public required string Peer { get; init; }
    public required SyncMode Mode { get; init; }
}

public enum LedgerDirection
{
    Sent,
    Applied
}

public sealed record LedgerEvent
{
    public required DateTimeOffset AtUtc { get; init; }
    public required LedgerDirection Direction { get; init; }
    public required string? LocalPosition { get; init; }
    public required string? PeerPosition { get; init; }
    public required string? PackageDigest { get; init; }
}

public sealed record StoreState
{
    public required int SchemaVersion { get; init; }
    public required ChannelConfig Channel { get; init; }
    public required IReadOnlyList<LedgerEvent> Ledger { get; init; }
    public required Dictionary<string, string> AppliedFileHashes { get; init; }
    public required Dictionary<string, byte[]> AppliedContents { get; init; }
}
