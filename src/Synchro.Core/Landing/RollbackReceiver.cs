using Synchro.Core;
using Synchro.Core.Store;

namespace Synchro.Core.Landing;

public static class RollbackReceiver
{
    public static Result<string> Resolve(string masterRoot, SlaveConfig slave, bool preferMaster)
    {
        var masterOnSyncBranch = ReceivingGit.CurrentBranch(masterRoot)
            is Result<string>.Ok branch && branch.Value == slave.SyncBranch;

        if (preferMaster)
        {
            return masterOnSyncBranch
                ? new Result<string>.Ok(masterRoot)
                : new Result<string>.Fail(SyncError.Invalid(
                    $"'--master' expects the master repo at '{masterRoot}' on '{slave.SyncBranch}' — it is on another branch, so there is no inbox to roll back here"));
        }

        var slaveAvailable = Directory.Exists(slave.Root);
        if (!masterOnSyncBranch)
        {
            return slaveAvailable
                ? new Result<string>.Ok(slave.Root)
                : new Result<string>.Fail(SyncError.NotFound($"slave folder is missing: '{slave.Root}'"));
        }

        return slaveAvailable
            ? new Result<string>.Fail(SyncError.Conflict(
                $"ambiguous rollback target: the master repo at '{masterRoot}' sits on '{slave.SyncBranch}' and the slave folder '{slave.Root}' also exists — run from the slave root, or pass '--master' to roll back the master copy"))
            : new Result<string>.Ok(masterRoot);
    }
}
