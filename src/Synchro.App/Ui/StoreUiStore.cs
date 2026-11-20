using Synchro.App.Commands;
using Synchro.Core;
using Synchro.Core.Store;
using Synchro.Core.Sync;

namespace Synchro.App.Ui;

internal sealed class StoreUiStore : IUiStore
{
    private const int ConflictHistoryCap = 20;

    private readonly string _root;

    public StoreUiStore(string root) => _root = root;

    public IReadOnlyList<SlaveView> GetSlaves()
    {
        var state = LoadState();
        if (state is null)
        {
            return [];
        }

        var views = new List<SlaveView>();
        foreach (var slave in state.Slaves.Values.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            views.Add(ToView(SyncStatusReader.Read(_root, state, slave)));
        }

        return views;
    }

    public IReadOnlyList<HistoryEntryView> GetHistory(string slaveName)
    {
        var state = LoadState();
        if (state is null || !state.Slaves.ContainsKey(slaveName))
        {
            return [];
        }

        return SyncHistoryLog.List(state, slaveName) is not Result<IReadOnlyList<SyncHistoryRecord>>.Ok history
            ? []
            : [.. history.Value.Select(ToEntry)];
    }

    public SlaveConflictView? GetConflicts(string slaveName)
    {
        var state = LoadState();
        if (state is null || !state.Slaves.ContainsKey(slaveName))
        {
            return null;
        }

        if (SyncHistoryLog.List(state, slaveName) is not Result<IReadOnlyList<SyncHistoryRecord>>.Ok history)
        {
            return null;
        }

        var conflicts = history.Value
            .SelectMany(record => record.Conflicts.Select(conflict => new ConflictPathView(
                conflict.Path,
                conflict.Winner == ConflictWinner.Master ? "master-wins" : "slave-wins",
                string.Empty,
                string.Empty,
                string.Empty)))
            .Take(ConflictHistoryCap)
            .ToList();

        return conflicts.Count == 0 ? null : new SlaveConflictView(slaveName, conflicts);
    }

    private StoreState? LoadState() =>
        MasterStore.Load(_root) is Result<(StoreState State, byte[] Key)>.Ok loaded ? loaded.Value.State : null;

    private static SlaveView ToView(SlaveStatus status)
    {
        var config = status.Config;
        return new SlaveView(
            config.Name,
            config.Root,
            config.SyncBranch,
            config.BaseBranch,
            SyncStatusFormatter.Hint(status.Hint),
            InboxViewOf(status.SlaveInbox),
            InboxViewOf(status.MasterInbox),
            status.InFlight,
            SyncStatusFormatter.Operation(status.LastOperation));
    }

    private static InboxView InboxViewOf(InboxState? inbox) =>
        inbox is null
            ? new InboxView("missing", null, 0, null)
            : new InboxView(
                inbox.OnSyncBranch ? (inbox.DirtyFiles > 0 ? "pending" : "open") : "clean",
                inbox.Branch,
                inbox.DirtyFiles,
                null);

    private static HistoryEntryView ToEntry(SyncHistoryRecord record) =>
        new(
            record.AtUtc,
            record.Direction == SyncDirection.MasterToSlave ? "master → slave" : "slave → master",
            record.FilesChanged,
            record.Conflicts.Count == 0
                ? null
                : $"{record.Conflicts.Count} conflict(s): {string.Join(", ", record.Conflicts.Select(ConflictText))}");

    private static string ConflictText(SyncConflict conflict) =>
        $"{conflict.Path} ({(conflict.Winner == ConflictWinner.Master ? "master wins" : "slave wins")})";
}
