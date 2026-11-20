using Synchro.Core;
using Synchro.Core.Git;

namespace Synchro.Core.Landing;

public static class SyncPreconditions
{
    public static SyncError? Check(
        string receivingRoot, string markerPath, string syncBranch, string baseBranch)
    {
        if (InFlightMarker.Exists(markerPath))
        {
            return SyncError.Conflict(
                $"a sync is already in flight (marker '{markerPath}') — another sync is running or crashed mid-run; sort it out, then delete the marker");
        }

        var current = ReceivingGit.CurrentBranch(receivingRoot);
        if (current is not Result<string>.Ok currentOk)
        {
            return SyncError.Conflict(
                $"receiving repo is not on a branch ({((Result<string>.Fail)current).Error.Message}) — sync needs '{baseBranch}' checked out");
        }

        var branch = currentOk.Value;
        if (branch == syncBranch)
        {
            return SyncError.Conflict(
                $"inbox on '{syncBranch}' is still open — commit it or run 'synchro rollback' before syncing again");
        }

        if (branch != baseBranch)
        {
            return SyncError.Conflict(
                $"receiving repo is on '{branch}' — sync needs the base branch '{baseBranch}' checked out");
        }

        if (GitChangeService.IsDirty(receivingRoot))
        {
            return SyncError.Conflict(
                $"working tree has uncommitted changes — commit or stash them first; '{baseBranch}' must be clean to receive a landing");
        }

        return null;
    }
}
