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

public enum SyncDirection
{
    MasterToSlave,
    SlaveToMaster
}

public enum ConflictWinner
{
    Master,
    Slave
}

public sealed record SlaveConfig
{
    public required string Name { get; init; }
    public required string Root { get; init; }
    public required string SyncBranch { get; init; }
    public required string BaseBranch { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record SyncConflict
{
    public required string Path { get; init; }
    public required ConflictWinner Winner { get; init; }
}

public sealed record SyncHistoryRecord
{
    public required DateTimeOffset AtUtc { get; init; }
    public required SyncDirection Direction { get; init; }
    public required int FilesChanged { get; init; }
    public required IReadOnlyList<SyncConflict> Conflicts { get; init; }
    public required bool RolledBack { get; init; }
}

public sealed record StoreState
{
    public required int SchemaVersion { get; init; }
    public required ChannelConfig Channel { get; init; }
    public required IReadOnlyList<LedgerEvent> Ledger { get; init; }
    public required Dictionary<string, string> AppliedFileHashes { get; init; }
    public required Dictionary<string, byte[]> AppliedContents { get; init; }

    public string? PeerKnownLocalPosition { get; init; }
    public string? PeerHeadPosition { get; init; }

    public Dictionary<string, string> SentFileHashes { get; init; } = [];
    public int SentChainSeq { get; init; }
    public string? SentChainRootHash { get; init; }

    public int AppliedChainSeq { get; init; }
    public int PeerAppliedChainSeq { get; init; }
    public PendingSceneState? PendingScene { get; init; }

    public Dictionary<string, SlaveConfig> Slaves { get; init; } = [];
    public Dictionary<string, byte[]> LedgerHints { get; init; } = [];
    public Dictionary<string, IReadOnlyList<SyncHistoryRecord>> SyncHistory { get; init; } = [];
}
