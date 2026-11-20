using Synchro.Core;
using Synchro.Core.Git;
using Synchro.Core.Landing;
using Synchro.Core.Store;

namespace Synchro.Core.Sync;

public static class SyncChangeMapper
{
    public static Result<SyncLandingSet> Build(
        SyncPlan plan,
        string masterRoot,
        string slaveRoot,
        IReadOnlyDictionary<string, ConflictWinner> resolutions,
        IReadOnlySet<string> committedInMasterInbox)
    {
        var toSlave = new List<LandingChange>();
        var toMaster = new List<LandingChange>();

        foreach (var delta in plan.FileMap)
        {
            if (committedInMasterInbox.Contains(delta.Path))
            {
                continue;
            }

            switch (delta.Kind)
            {
                case FileDeltaKind.MasterToSlave:
                    var masterChange = Change(masterRoot, plan.MasterHead, delta, delta.MasterHash is not null);
                    if (masterChange is not Result<LandingChange>.Ok masterOk)
                    {
                        return new Result<SyncLandingSet>.Fail(((Result<LandingChange>.Fail)masterChange).Error);
                    }

                    toSlave.Add(masterOk.Value);
                    break;

                case FileDeltaKind.SlaveToMaster:
                    var slaveChange = Change(slaveRoot, plan.SlaveHead, delta, delta.SlaveHash is not null);
                    if (slaveChange is not Result<LandingChange>.Ok slaveOk)
                    {
                        return new Result<SyncLandingSet>.Fail(((Result<LandingChange>.Fail)slaveChange).Error);
                    }

                    toMaster.Add(slaveOk.Value);
                    break;

                case FileDeltaKind.Conflict:
                    if (!resolutions.TryGetValue(delta.Path, out var winner))
                    {
                        return new Result<SyncLandingSet>.Fail(SyncError.Invalid(
                            $"'{delta.Path}' conflicts but has no resolution — resolve it before mapping"));
                    }

                    var sourceRoot = winner == ConflictWinner.Master ? masterRoot : slaveRoot;
                    var sourceHead = winner == ConflictWinner.Master ? plan.MasterHead : plan.SlaveHead;
                    var sourceHoldsFile = winner == ConflictWinner.Master
                        ? delta.MasterHash is not null
                        : delta.SlaveHash is not null;
                    var change = Change(sourceRoot, sourceHead, delta, sourceHoldsFile);
                    if (change is not Result<LandingChange>.Ok changeOk)
                    {
                        return new Result<SyncLandingSet>.Fail(((Result<LandingChange>.Fail)change).Error);
                    }

                    if (winner == ConflictWinner.Master)
                    {
                        toSlave.Add(changeOk.Value);
                    }
                    else
                    {
                        toMaster.Add(changeOk.Value);
                    }

                    break;
            }
        }

        return new Result<SyncLandingSet>.Ok(new SyncLandingSet { ToSlave = toSlave, ToMaster = toMaster });
    }

    private static Result<LandingChange> Change(string root, string head, FileDelta delta, bool sourceHoldsFile)
    {
        var content = GitChangeService.GetFileContent(root, delta.Path, head);
        if (content is Result<byte[]>.Ok ok)
        {
            return new Result<LandingChange>.Ok(new LandingChange.Write(delta.Path, ok.Value));
        }

        return sourceHoldsFile
            ? new Result<LandingChange>.Fail(((Result<byte[]>.Fail)content).Error)
            : new Result<LandingChange>.Ok(new LandingChange.Delete(delta.Path));
    }
}
