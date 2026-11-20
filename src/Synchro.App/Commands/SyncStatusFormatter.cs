using Synchro.Core.Store;
using Synchro.Core.Sync;

namespace Synchro.App.Commands;

internal static class SyncStatusFormatter
{
    public static string Hint(LedgerHint? hint)
    {
        if (hint is null)
        {
            return "none yet — the first sync builds it";
        }

        var deSync = hint.DeSync ? ", de-sync detected — the next sync rebuilds by content" : string.Empty;
        return $"{hint.Relation.ToString().ToLowerInvariant()}, master {ShortSha(hint.MasterHead)}, "
               + $"slave {ShortSha(hint.SlaveHead)}, {hint.PendingShas.Count} pending, "
               + $"{hint.ConsumedShas.Count} consumed{deSync}";
    }

    public static string Inbox(string syncBranch, InboxState? inbox)
    {
        if (inbox is null)
        {
            return "repo missing — check the path";
        }

        if (inbox.OnSyncBranch)
        {
            return $"on '{inbox.Branch}', {inbox.DirtyFiles} uncommitted file(s) — inbox open (commit it or run rollback)";
        }

        return inbox.DirtyFiles > 0
            ? $"on '{inbox.Branch}', {inbox.DirtyFiles} uncommitted file(s) — commit or stash before syncing"
            : $"on '{inbox.Branch}', clean";
    }

    public static string Operation(SyncHistoryRecord? record)
    {
        if (record is null)
        {
            return "none yet";
        }

        var direction = record.Direction == SyncDirection.MasterToSlave ? "master→slave" : "slave→master";
        var conflicts = record.Conflicts.Count == 0
            ? "no conflicts"
            : $"{record.Conflicts.Count} conflict(s): {string.Join(", ", record.Conflicts.Select(ConflictText))}";
        return $"{record.AtUtc:yyyy-MM-dd HH:mm:ss}Z {direction} — {record.FilesChanged} file(s), {conflicts}";
    }

    public static string ShortSha(string sha) => sha[..Math.Min(7, sha.Length)];

    private static string ConflictText(SyncConflict conflict) =>
        $"{conflict.Path} ({(conflict.Winner == ConflictWinner.Master ? "master wins" : "slave wins")})";
}
