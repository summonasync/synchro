using Synchro.Core;

namespace Synchro.Core.Sync;

public static class SyncEngine
{
    public static Result<SyncPlan> Reconcile(ReconcileRequest request)
    {
        var masterHead = CommitGraph.ResolveBranch(request.MasterRoot, request.MasterBaseBranch);
        if (masterHead is null)
        {
            return new Result<SyncPlan>.Fail(
                SyncError.Invalid($"master branch '{request.MasterBaseBranch}' not found in the master repo"));
        }

        var slaveHead = CommitGraph.ResolveBranch(request.SlaveRoot, request.SlaveBaseBranch);
        if (slaveHead is null)
        {
            return new Result<SyncPlan>.Fail(
                SyncError.Invalid($"slave branch '{request.SlaveBaseBranch}' not found in the slave repo"));
        }

        var slaveSyncHead = CommitGraph.ResolveBranch(request.SlaveRoot, request.SlaveSyncBranch);
        var slaveObjects = CommitGraph.ObjectsDir(request.SlaveRoot);
        if (slaveObjects is not Result<string>.Ok objectsOk)
        {
            return new Result<SyncPlan>.Fail(((Result<string>.Fail)slaveObjects).Error);
        }

        var mergeBase = CommitGraph.MergeBase(request.MasterRoot, objectsOk.Value, masterHead, slaveHead);
        if (mergeBase is not Result<string?>.Ok baseOk)
        {
            return new Result<SyncPlan>.Fail(((Result<string?>.Fail)mergeBase).Error);
        }

        return baseOk.Value is null
            ? ReconcileUnrelated(request, masterHead, slaveHead, slaveSyncHead)
            : ReconcileRelated(request, masterHead, slaveHead, slaveSyncHead, baseOk.Value, objectsOk.Value);
    }

    private static Result<SyncPlan> ReconcileUnrelated(
        ReconcileRequest request, string masterHead, string slaveHead, string? slaveSyncHead)
    {
        var deltas = TreeContent.CompareTrees(request.MasterRoot, masterHead, request.SlaveRoot, slaveHead);
        if (deltas is not Result<IReadOnlyList<FileDelta>>.Ok deltasOk)
        {
            return new Result<SyncPlan>.Fail(((Result<IReadOnlyList<FileDelta>>.Fail)deltas).Error);
        }

        return new Result<SyncPlan>.Ok(Assemble(
            HistoryRelation.Unrelated, null, request, masterHead, slaveHead, slaveSyncHead,
            deSync: false, [], [], deltasOk.Value));
    }

    private static Result<SyncPlan> ReconcileRelated(
        ReconcileRequest request, string masterHead, string slaveHead, string? slaveSyncHead,
        string mergeBase, string slaveObjectsDir)
    {
        var masterShas = CommitGraph.Reachable(request.MasterRoot, slaveObjectsDir, masterHead, slaveHead);
        if (masterShas is not Result<IReadOnlyList<string>>.Ok masterShasOk)
        {
            return new Result<SyncPlan>.Fail(((Result<IReadOnlyList<string>>.Fail)masterShas).Error);
        }

        var slaveShas = CommitGraph.Reachable(request.MasterRoot, slaveObjectsDir, slaveHead, masterHead);
        if (slaveShas is not Result<IReadOnlyList<string>>.Ok slaveShasOk)
        {
            return new Result<SyncPlan>.Fail(((Result<IReadOnlyList<string>>.Fail)slaveShas).Error);
        }

        var masterCommits = LoadCommits(request.MasterRoot, masterShasOk.Value);
        if (masterCommits is not Result<IReadOnlyList<SyncCommit>>.Ok masterOk)
        {
            return new Result<SyncPlan>.Fail(((Result<IReadOnlyList<SyncCommit>>.Fail)masterCommits).Error);
        }

        var slaveCommits = LoadCommits(request.SlaveRoot, slaveShasOk.Value);
        if (slaveCommits is not Result<IReadOnlyList<SyncCommit>>.Ok slaveOk)
        {
            return new Result<SyncPlan>.Fail(((Result<IReadOnlyList<SyncCommit>>.Fail)slaveCommits).Error);
        }

        var syncCommits = slaveSyncHead is null
            ? new Result<IReadOnlyList<SyncCommit>>.Ok([])
            : TruthReader.Read(request.SlaveRoot, request.SlaveSyncBranch, request.SlaveBaseBranch);
        if (syncCommits is not Result<IReadOnlyList<SyncCommit>>.Ok syncOk)
        {
            return new Result<SyncPlan>.Fail(((Result<IReadOnlyList<SyncCommit>>.Fail)syncCommits).Error);
        }

        var (masterLeft, slaveLeft) = MatchByPatchId(masterOk.Value, slaveOk.Value);
        var offers = masterLeft
            .Select(m => new CommitDisposition { Commit = m, Disposition = TruthReader.Classify(m, syncOk.Value, request.MasterRoot) })
            .ToList();
        var reoffers = offers
            .Where(o => o.Disposition == OfferDisposition.Reoffer)
            .Select(o => o.Commit)
            .ToList();

        var touchedOnMaster = reoffers.SelectMany(c => c.Files).ToHashSet(StringComparer.Ordinal);
        var touchedOnSlave = slaveLeft.SelectMany(c => c.Files).ToHashSet(StringComparer.Ordinal);
        var touched = touchedOnMaster.Union(touchedOnSlave).OrderBy(p => p, StringComparer.Ordinal).ToList();

        var touchedMap = TreeContent.CompareTouched(
            request.MasterRoot, masterHead, request.SlaveRoot, slaveHead, touched, touchedOnMaster, touchedOnSlave);
        if (touchedMap is not Result<IReadOnlyList<FileDelta>>.Ok touchedOk)
        {
            return new Result<SyncPlan>.Fail(((Result<IReadOnlyList<FileDelta>>.Fail)touchedMap).Error);
        }

        var deSync = touchedOk.Value.Any(d => d.Kind == FileDeltaKind.Conflict);
        IReadOnlyList<FileDelta> fileMap = touchedOk.Value;
        if (deSync)
        {
            var rebuild = TreeContent.RebuildTrees(
                request.MasterRoot, masterHead, request.SlaveRoot, slaveHead, touchedOnMaster, touchedOnSlave);
            if (rebuild is not Result<IReadOnlyList<FileDelta>>.Ok rebuildOk)
            {
                return new Result<SyncPlan>.Fail(((Result<IReadOnlyList<FileDelta>>.Fail)rebuild).Error);
            }

            fileMap = rebuildOk.Value;
        }

        return new Result<SyncPlan>.Ok(Assemble(
            HistoryRelation.Related, mergeBase, request, masterHead, slaveHead, slaveSyncHead,
            deSync, offers, slaveLeft, fileMap));
    }

    private static SyncPlan Assemble(
        HistoryRelation relation, string? mergeBase, ReconcileRequest request,
        string masterHead, string slaveHead, string? slaveSyncHead,
        bool deSync, IReadOnlyList<CommitDisposition> masterOffers,
        IReadOnlyList<SyncCommit> slaveOffers, IReadOnlyList<FileDelta> fileMap)
    {
        return new SyncPlan
        {
            Relation = relation,
            MergeBase = mergeBase,
            MasterHead = masterHead,
            SlaveHead = slaveHead,
            SlaveSyncHead = slaveSyncHead,
            DeSync = deSync,
            NoOp = fileMap.Count == 0,
            MasterOffers = masterOffers,
            SlaveOffers = slaveOffers,
            FileMap = fileMap,
            Hint = new LedgerHint
            {
                Relation = relation,
                MergeBase = mergeBase,
                MasterHead = masterHead,
                SlaveHead = slaveHead,
                SlaveSyncHead = slaveSyncHead,
                ConsumedShas = [.. masterOffers
                    .Where(o => o.Disposition != OfferDisposition.Reoffer)
                    .Select(o => o.Commit.Sha)
                    .OrderBy(s => s, StringComparer.Ordinal)],
                PendingShas = [.. masterOffers
                    .Where(o => o.Disposition == OfferDisposition.Reoffer)
                    .Select(o => o.Commit.Sha)
                    .OrderBy(s => s, StringComparer.Ordinal)],
                DeSync = deSync
            }
        };
    }

    private static Result<IReadOnlyList<SyncCommit>> LoadCommits(string repo, IReadOnlyList<string> shas)
    {
        var commits = new List<SyncCommit>();
        foreach (var sha in shas)
        {
            var commit = TruthReader.Load(repo, sha);
            if (commit is not Result<SyncCommit>.Ok commitOk)
            {
                return new Result<IReadOnlyList<SyncCommit>>.Fail(((Result<SyncCommit>.Fail)commit).Error);
            }

            commits.Add(commitOk.Value);
        }

        return new Result<IReadOnlyList<SyncCommit>>.Ok(commits);
    }

    private static (IReadOnlyList<SyncCommit> MasterLeft, IReadOnlyList<SyncCommit> SlaveLeft) MatchByPatchId(
        IReadOnlyList<SyncCommit> masterCommits, IReadOnlyList<SyncCommit> slaveCommits)
    {
        var matchedSlaves = new HashSet<string>(StringComparer.Ordinal);
        var masterLeft = new List<SyncCommit>();
        foreach (var master in masterCommits)
        {
            var twin = master.PatchId is null
                ? null
                : slaveCommits.FirstOrDefault(s => s.PatchId == master.PatchId && !matchedSlaves.Contains(s.Sha));
            if (twin is null)
            {
                masterLeft.Add(master);
            }
            else
            {
                matchedSlaves.Add(twin.Sha);
            }
        }

        var slaveLeft = slaveCommits.Where(s => !matchedSlaves.Contains(s.Sha)).ToList();
        return (masterLeft, slaveLeft);
    }
}
