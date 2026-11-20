using Synchro.Core;
using Synchro.Core.Store;

namespace Synchro.Core.Landing;

public static class RollbackService
{
    public static Result<RollbackReport> Rollback(string receivingRoot, string storeRoot, SlaveConfig slave)
    {
        var current = ReceivingGit.CurrentBranch(receivingRoot);
        if (current is not Result<string>.Ok currentOk)
        {
            return new Result<RollbackReport>.Fail(((Result<string>.Fail)current).Error);
        }

        var branch = currentOk.Value;
        if (branch == slave.BaseBranch)
        {
            return OnBaseBranch(receivingRoot, slave);
        }

        if (branch != slave.SyncBranch)
        {
            return new Result<RollbackReport>.Fail(SyncError.Conflict(
                $"receiving repo is on '{branch}' — rollback works from the sync branch '{slave.SyncBranch}'"));
        }

        return DiscardInbox(receivingRoot, storeRoot, slave);
    }

    private static Result<RollbackReport> OnBaseBranch(string receivingRoot, SlaveConfig slave)
    {
        var state = ReceivingGit.State(receivingRoot);
        if (state is not Result<WorktreeState>.Ok stateOk)
        {
            return new Result<RollbackReport>.Fail(((Result<WorktreeState>.Fail)state).Error);
        }

        if (!stateOk.Value.IsClean)
        {
            return new Result<RollbackReport>.Fail(SyncError.Conflict(
                $"working tree on '{slave.BaseBranch}' carries uncommitted non-sync changes — nothing to roll back; resolve them first"));
        }

        return new Result<RollbackReport>.Ok(new RollbackReport
        {
            RolledBack = false,
            Branch = slave.BaseBranch,
            RestoredPaths = [],
            RemovedPaths = []
        });
    }

    private static Result<RollbackReport> DiscardInbox(string receivingRoot, string storeRoot, SlaveConfig slave)
    {
        var state = ReceivingGit.State(receivingRoot);
        if (state is not Result<WorktreeState>.Ok stateOk)
        {
            return new Result<RollbackReport>.Fail(((Result<WorktreeState>.Fail)state).Error);
        }

        var worktree = stateOk.Value;
        if (worktree.IsClean)
        {
            return new Result<RollbackReport>.Fail(SyncError.Conflict(
                $"nothing dirty to discard on '{slave.SyncBranch}' — the inbox was committed, so the rollback window is closed; undo it with 'git revert' on '{slave.SyncBranch}'"));
        }

        var removed = new List<string>();
        foreach (var path in worktree.Untracked)
        {
            if (path.EndsWith('/'))
            {
                Directory.Delete(Path.Combine(receivingRoot, path.TrimEnd('/')), recursive: true);
            }
            else
            {
                File.Delete(Path.Combine(receivingRoot, path.Replace('/', Path.DirectorySeparatorChar)));
                ReceivingGit.PruneEmptyParents(receivingRoot, path);
            }

            removed.Add(path);
        }

        var discardError = ReceivingGit.DiscardAll(receivingRoot);
        if (discardError is not null)
        {
            return new Result<RollbackReport>.Fail(discardError);
        }

        var checkout = ReceivingGit.Checkout(receivingRoot, slave.BaseBranch);
        if (checkout is not Result<string>.Ok)
        {
            return new Result<RollbackReport>.Fail(((Result<string>.Fail)checkout).Error);
        }

        InFlightMarker.Clear(InFlightMarker.PathFor(storeRoot, slave.Name));

        return new Result<RollbackReport>.Ok(new RollbackReport
        {
            RolledBack = true,
            Branch = slave.BaseBranch,
            RestoredPaths = worktree.TrackedDirty,
            RemovedPaths = removed
        });
    }
}
