namespace Synchro.App.Ui;

internal sealed record SlaveView(
    string Name,
    string Path,
    string SyncBranch,
    string BaseBranch,
    string LedgerHint,
    InboxView SlaveInbox,
    InboxView MasterInbox,
    bool SyncInFlight,
    string LastOperation);

internal sealed record InboxView(
    string State,
    string? Branch,
    int DirtyFileCount,
    string? LastSyncOp);

internal sealed record HistoryEntryView(
    DateTimeOffset AtUtc,
    string Direction,
    int FileCount,
    string? ConflictResolution);

internal sealed record ConflictPathView(
    string Path,
    string Resolution,
    string Theirs,
    string Local,
    string Base);

internal sealed record SlaveConflictView(string SlaveName, IReadOnlyList<ConflictPathView> Paths);
