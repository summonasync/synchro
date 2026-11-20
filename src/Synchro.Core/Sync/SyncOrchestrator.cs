using Synchro.Core;
using Synchro.Core.Landing;
using Synchro.Core.Store;

namespace Synchro.Core.Sync;

public static class SyncOrchestrator
{
    public static Result<SyncRunOutput> Run(
        string masterRoot,
        StoreState state,
        SlaveConfig slave,
        ISyncUi ui,
        bool nonInteractive)
    {
        if (!Directory.Exists(slave.Root))
        {
            return new Result<SyncRunOutput>.Fail(SyncError.Invalid(
                $"slave repo missing at '{slave.Root}' — check the path or re-add the slave"));
        }

        var branch = ReceivingGit.CurrentBranch(masterRoot);
        if (branch is not Result<string>.Ok branchOk)
        {
            return new Result<SyncRunOutput>.Fail(((Result<string>.Fail)branch).Error);
        }

        var markerPath = InFlightMarker.PathFor(masterRoot, slave.Name);
        var refusal = SyncPreconditions.Check(slave.Root, markerPath, slave.SyncBranch, slave.BaseBranch)
                      ?? SyncPreconditions.Check(masterRoot, markerPath, slave.SyncBranch, branchOk.Value);
        if (refusal is not null)
        {
            return new Result<SyncRunOutput>.Fail(refusal);
        }

        var plan = SyncEngine.Reconcile(new ReconcileRequest
        {
            MasterRoot = masterRoot,
            MasterBaseBranch = branchOk.Value,
            SlaveRoot = slave.Root,
            SlaveBaseBranch = slave.BaseBranch,
            SlaveSyncBranch = slave.SyncBranch
        });
        if (plan is not Result<SyncPlan>.Ok planOk)
        {
            return new Result<SyncRunOutput>.Fail(((Result<SyncPlan>.Fail)plan).Error);
        }

        var syncTip = ReceivingGit.BranchTip(masterRoot, slave.SyncBranch);
        var committed = SyncInboxProbe.CommittedPaths(
            masterRoot,
            syncTip is Result<string>.Ok tip ? tip.Value : null,
            planOk.Value.FileMap);

        var conflicts = planOk.Value.FileMap
            .Where(delta => delta.Kind == FileDeltaKind.Conflict && !committed.Contains(delta.Path))
            .ToList();
        var resolutions = Resolve(conflicts, ui, nonInteractive);

        var set = SyncChangeMapper.Build(planOk.Value, masterRoot, slave.Root, resolutions, committed);
        if (set is not Result<SyncLandingSet>.Ok setOk)
        {
            return new Result<SyncRunOutput>.Fail(((Result<SyncLandingSet>.Fail)set).Error);
        }

        LandingReport? toSlave = null;
        if (setOk.Value.ToSlave.Count > 0)
        {
            var landing = LandingService.Land(slave.Root, masterRoot, slave, setOk.Value.ToSlave);
            if (landing is not Result<LandingReport>.Ok slaveLanded)
            {
                return new Result<SyncRunOutput>.Fail(((Result<LandingReport>.Fail)landing).Error);
            }

            toSlave = slaveLanded.Value;
        }

        var masterReceiver = slave with { Root = masterRoot, BaseBranch = branchOk.Value };
        LandingReport? toMaster = null;
        if (setOk.Value.ToMaster.Count > 0)
        {
            var landing = LandingService.Land(masterRoot, masterRoot, masterReceiver, setOk.Value.ToMaster);
            if (landing is not Result<LandingReport>.Ok masterLanded)
            {
                return new Result<SyncRunOutput>.Fail(((Result<LandingReport>.Fail)landing).Error);
            }

            toMaster = masterLanded.Value;
        }

        var report = new SyncRunReport
        {
            SlaveName = slave.Name,
            NonInteractive = nonInteractive,
            Conflicts = [.. conflicts.Select(delta => new SyncConflict
            {
                Path = delta.Path,
                Winner = resolutions[delta.Path]
            })],
            AlreadyCommitted = [.. committed],
            ToSlave = toSlave,
            ToMaster = toMaster
        };

        var persisted = SyncLedgerWriter.Persist(state, slave.Name, planOk.Value.Hint, BuildRecords(report));
        if (persisted is not Result<StoreState>.Ok persistedOk)
        {
            return new Result<SyncRunOutput>.Fail(((Result<StoreState>.Fail)persisted).Error);
        }

        return new Result<SyncRunOutput>.Ok(new SyncRunOutput { Report = report, State = persistedOk.Value });
    }

    private static Dictionary<string, ConflictWinner> Resolve(
        IReadOnlyList<FileDelta> conflicts, ISyncUi ui, bool nonInteractive)
    {
        var resolutions = new Dictionary<string, ConflictWinner>(StringComparer.Ordinal);
        foreach (var conflict in conflicts)
        {
            resolutions[conflict.Path] = nonInteractive ? ConflictWinner.Master : ui.ResolveConflict(conflict.Path);
        }

        return resolutions;
    }

    private static IReadOnlyList<SyncHistoryRecord> BuildRecords(SyncRunReport report)
    {
        var at = DateTimeOffset.UtcNow;
        return
        [
            new SyncHistoryRecord
            {
                AtUtc = at,
                Direction = SyncDirection.MasterToSlave,
                FilesChanged = FilesLanded(report.ToSlave),
                Conflicts = [.. report.Conflicts.Where(c => c.Winner == ConflictWinner.Master)],
                RolledBack = false
            },
            new SyncHistoryRecord
            {
                AtUtc = at,
                Direction = SyncDirection.SlaveToMaster,
                FilesChanged = FilesLanded(report.ToMaster),
                Conflicts = [.. report.Conflicts.Where(c => c.Winner == ConflictWinner.Slave)],
                RolledBack = false
            }
        ];
    }

    private static int FilesLanded(LandingReport? landing) =>
        landing is null ? 0 : landing.WrittenFiles.Count + landing.DeletedFiles.Count;
}
