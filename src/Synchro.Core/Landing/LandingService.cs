using Synchro.Core;
using Synchro.Core.Store;

namespace Synchro.Core.Landing;

public static class LandingService
{
    public static Result<LandingReport> Land(
        string receivingRoot,
        string storeRoot,
        SlaveConfig slave,
        IReadOnlyList<LandingChange> changes)
    {
        var markerPath = InFlightMarker.PathFor(storeRoot, slave.Name);
        var refusal = SyncPreconditions.Check(receivingRoot, markerPath, slave.SyncBranch, slave.BaseBranch);
        if (refusal is not null)
        {
            return new Result<LandingReport>.Fail(refusal);
        }

        InFlightMarker.Set(markerPath);
        try
        {
            var branchCreated = !ReceivingGit.BranchExists(receivingRoot, slave.SyncBranch);
            var branch = branchCreated
                ? ReceivingGit.CreateBranch(receivingRoot, slave.SyncBranch, slave.BaseBranch)
                : ReceivingGit.Checkout(receivingRoot, slave.SyncBranch);
            if (branch is not Result<string>.Ok branchOk)
            {
                return new Result<LandingReport>.Fail(((Result<string>.Fail)branch).Error);
            }

            var edits = WorkingTreeWriter.Apply(receivingRoot, changes);
            if (edits is not Result<LandingEdits>.Ok editsOk)
            {
                return new Result<LandingReport>.Fail(((Result<LandingEdits>.Fail)edits).Error);
            }

            return new Result<LandingReport>.Ok(new LandingReport
            {
                BranchCreated = branchCreated,
                Branch = branchOk.Value,
                WrittenFiles = editsOk.Value.Written,
                DeletedFiles = editsOk.Value.Deleted
            });
        }
        finally
        {
            InFlightMarker.Clear(markerPath);
        }
    }
}
