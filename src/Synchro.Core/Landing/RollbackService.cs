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
            return OnBaseBranch(receivingRoot, storeRoot, slave);
        }

        if (branch != slave.SyncBranch)
        {
            return new Result<RollbackReport>.Fail(SyncError.Conflict(
                $"receiving repo is on '{branch}' — rollback works from the sync branch '{slave.SyncBranch}'"));
        }

        return DiscardInbox(receivingRoot, storeRoot, slave);
    }

    private static Result<RollbackReport> OnBaseBranch(string receivingRoot, string storeRoot, SlaveConfig slave)
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

        InFlightMarker.Clear(InFlightMarker.PathFor(storeRoot, slave.Name));

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
            return NothingToDiscard(receivingRoot, storeRoot, slave);
        }

        var removed = new List<string>();
        var skipped = new List<RollbackReport.SkippedRemoval>();
        foreach (var path in worktree.Untracked)
        {
            try
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new RollbackReport.SkippedRemoval(path, ex.Message));
            }
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
            RemovedPaths = removed,
            Skipped = skipped
        });
    }

    private static Result<RollbackReport> NothingToDiscard(string receivingRoot, string storeRoot, SlaveConfig slave)
    {
        var syncTip = ReceivingGit.BranchTip(receivingRoot, slave.SyncBranch);
        if (syncTip is not Result<string>.Ok syncOk)
        {
            return new Result<RollbackReport>.Fail(((Result<string>.Fail)syncTip).Error);
        }

        var baseTip = ReceivingGit.BranchTip(receivingRoot, slave.BaseBranch);
        if (baseTip is not Result<string>.Ok baseOk)
        {
            return new Result<RollbackReport>.Fail(((Result<string>.Fail)baseTip).Error);
        }

        if (syncOk.Value != baseOk.Value)
        {
            return new Result<RollbackReport>.Fail(SyncError.Conflict(
                $"nothing dirty to discard on '{slave.SyncBranch}' — the inbox was committed, so the rollback window is closed; undo it with 'git revert' on '{slave.SyncBranch}'"));
        }

        var checkout = ReceivingGit.Checkout(receivingRoot, slave.BaseBranch);
        if (checkout is not Result<string>.Ok)
        {
            return new Result<RollbackReport>.Fail(((Result<string>.Fail)checkout).Error);
        }

        InFlightMarker.Clear(InFlightMarker.PathFor(storeRoot, slave.Name));

        return new Result<RollbackReport>.Ok(new RollbackReport
        {
            RolledBack = false,
            Branch = slave.BaseBranch,
            RestoredPaths = [],
            RemovedPaths = []
        });
    }
}
