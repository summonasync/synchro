using System.Text;
using Synchro.Core;
using Synchro.Core.Git;
using Synchro.Core.Landing;
using Synchro.Core.Store;

namespace Synchro.Core.Sync;

public static class SyncStatusReader
{
    public static SlaveStatus Read(string masterRoot, StoreState state, SlaveConfig slave)
    {
        LedgerHint? hint = null;
        if (SlaveHints.Get(state, slave.Name) is Result<byte[]>.Ok hintOk)
        {
            hint = LedgerHint.Parse(Encoding.UTF8.GetString(hintOk.Value));
        }

        SyncHistoryRecord? lastOperation = null;
        if (SyncHistoryLog.List(state, slave.Name) is Result<IReadOnlyList<SyncHistoryRecord>>.Ok history)
        {
            lastOperation = history.Value.FirstOrDefault();
        }

        return new SlaveStatus
        {
            Config = slave,
            Hint = hint,
            SlaveInbox = Inbox(slave.Root, slave.SyncBranch),
            MasterInbox = Inbox(masterRoot, slave.SyncBranch),
            InFlight = InFlightMarker.Exists(InFlightMarker.PathFor(masterRoot, slave.Name)),
            LastOperation = lastOperation
        };
    }

    private static InboxState? Inbox(string root, string syncBranch)
    {
        if (!GitRepoLayout.IsWorkTree(root))
        {
            return null;
        }

        var branch = ReceivingGit.CurrentBranch(root);
        if (branch is not Result<string>.Ok branchOk)
        {
            return null;
        }

        var state = ReceivingGit.State(root);
        if (state is not Result<WorktreeState>.Ok stateOk)
        {
            return null;
        }

        return new InboxState
        {
            Branch = branchOk.Value,
            OnSyncBranch = branchOk.Value == syncBranch,
            DirtyFiles = stateOk.Value.TrackedDirty.Count + stateOk.Value.Untracked.Count
        };
    }
}
