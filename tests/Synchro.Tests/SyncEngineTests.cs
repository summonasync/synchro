using Shouldly;
using Synchro.Core;
using Synchro.Core.Hashing;
using Synchro.Core.Sync;
using Xunit;

namespace Synchro.Tests;

public class SyncEngineTests
{
    [Fact]
    public void Reconcile_BothSidesNew_ProducesTwoWayFileMapping()
    {
        var (master, slave, _) = CreatePair();
        try
        {
            TestGitRepo.Commit(master, "x.txt", "x\n", "master adds x");
            TestGitRepo.Commit(slave, "y.txt", "y\n", "slave adds y");

            var plan = Reconcile(Request(master, slave));

            plan.Relation.ShouldBe(HistoryRelation.Related);
            plan.DeSync.ShouldBeFalse();
            plan.NoOp.ShouldBeFalse();
            var offer = plan.MasterOffers.Single();
            offer.Disposition.ShouldBe(OfferDisposition.Reoffer);
            offer.Commit.Files.ShouldBe(["x.txt"]);
            plan.SlaveOffers.Single().Files.ShouldBe(["y.txt"]);

            var x = plan.FileMap.Single(d => d.Path == "x.txt");
            x.Kind.ShouldBe(FileDeltaKind.MasterToSlave);
            x.MasterHash.ShouldBe(ContentHash.Compute("x\n"u8.ToArray()));
            x.SlaveHash.ShouldBeNull();
            plan.FileMap.Single(d => d.Path == "y.txt").Kind.ShouldBe(FileDeltaKind.SlaveToMaster);

            plan.Hint.PendingShas.ShouldBe([offer.Commit.Sha]);
            plan.Hint.ConsumedShas.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(Directory.GetParent(master)!.FullName, true);
        }
    }

    [Fact]
    public void Reconcile_CherryPickedEqualPatches_NoOpWithoutOffers()
    {
        var (master, slave, _) = CreatePair();
        try
        {
            TestGitRepo.Commit(master, "f.txt", "v2\n", "master rework");
            TestGitRepo.Commit(slave, "f.txt", "v2\n", "same change, different message");

            var plan = Reconcile(Request(master, slave));

            plan.Relation.ShouldBe(HistoryRelation.Related);
            plan.DeSync.ShouldBeFalse();
            plan.NoOp.ShouldBeTrue();
            plan.MasterOffers.ShouldBeEmpty();
            plan.SlaveOffers.ShouldBeEmpty();
            plan.FileMap.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(Directory.GetParent(master)!.FullName, true);
        }
    }

    [Fact]
    public void Reconcile_SquashedDivergence_EqualContentArbitratesNoOp()
    {
        var (master, slave, baseSha) = CreatePair();
        try
        {
            TestGitRepo.Commit(master, "f.txt", "l1\n", "line one");
            TestGitRepo.Commit(master, "f.txt", "l1\nl2\n", "line two");
            TestGitRepo.Commit(slave, "f.txt", "l1\n", "line one copy");
            TestGitRepo.Commit(slave, "f.txt", "l1\nl2\n", "line two copy");

            GitCli.Run(master, "reset", "--soft", baseSha);
            GitCli.Run(master, "commit", "-m", "squashed");

            var plan = Reconcile(Request(master, slave));

            plan.DeSync.ShouldBeFalse();
            plan.NoOp.ShouldBeTrue();
            plan.FileMap.ShouldBeEmpty();
            plan.MasterOffers.Single().Disposition.ShouldBe(OfferDisposition.Reoffer);
            plan.SlaveOffers.Count.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(Directory.GetParent(master)!.FullName, true);
        }
    }

    [Fact]
    public void Reconcile_AppliedThenModified_ExcludedFromReoffer()
    {
        var (master, slave, _) = CreatePair();
        try
        {
            var offerSha = TestGitRepo.Commit(master, "f.txt", "v2\n", "master offer");
            CommitOnSyncBranch(slave, "f.txt", "v2 slave-tweaked\n", "inbox edit");

            var plan = Reconcile(Request(master, slave));

            plan.SlaveSyncHead.ShouldNotBeNull();
            plan.MasterOffers.Single().Disposition.ShouldBe(OfferDisposition.AppliedModified);
            plan.SlaveOffers.ShouldBeEmpty();
            plan.FileMap.ShouldBeEmpty();
            plan.NoOp.ShouldBeTrue();
            plan.DeSync.ShouldBeFalse();
            plan.Hint.ConsumedShas.ShouldBe([offerSha]);
            plan.Hint.PendingShas.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(Directory.GetParent(master)!.FullName, true);
        }
    }

    [Fact]
    public void Reconcile_AppliedAndCommittedOnSyncBranch_ClassifiedAppliedCommitted()
    {
        var (master, slave, _) = CreatePair();
        try
        {
            var offerSha = TestGitRepo.Commit(master, "f.txt", "v2\n", "master offer");
            CommitOnSyncBranch(slave, "f.txt", "v2\n", "inbox applied verbatim");

            var plan = Reconcile(Request(master, slave));

            plan.MasterOffers.Single().Disposition.ShouldBe(OfferDisposition.AppliedCommitted);
            plan.FileMap.ShouldBeEmpty();
            plan.NoOp.ShouldBeTrue();
            plan.Hint.ConsumedShas.ShouldBe([offerSha]);
            plan.Hint.PendingShas.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(Directory.GetParent(master)!.FullName, true);
        }
    }

    [Fact]
    public void Reconcile_DiscardedOffer_ReofferedNextSync()
    {
        var (master, slave, _) = CreatePair();
        try
        {
            var offerSha = TestGitRepo.Commit(master, "f.txt", "v2\n", "master offer");
            CommitOnSyncBranch(slave, "other.txt", "o\n", "unrelated inbox work");

            var plan = Reconcile(Request(master, slave));

            plan.MasterOffers.Single().Disposition.ShouldBe(OfferDisposition.Reoffer);
            plan.FileMap.Single().Path.ShouldBe("f.txt");
            plan.FileMap.Single().Kind.ShouldBe(FileDeltaKind.MasterToSlave);
            plan.NoOp.ShouldBeFalse();
            plan.Hint.PendingShas.ShouldBe([offerSha]);
        }
        finally
        {
            Directory.Delete(Directory.GetParent(master)!.FullName, true);
        }
    }

    [Fact]
    public void Reconcile_StructuralDivergence_DeSyncThenResyncAfterConvergence()
    {
        var (master, slave, _) = CreatePair();
        try
        {
            TestGitRepo.Commit(master, "f.txt", "from master\n", "master version");
            TestGitRepo.Commit(slave, "f.txt", "from slave\n", "slave version");

            var diverged = Reconcile(Request(master, slave));

            diverged.DeSync.ShouldBeTrue();
            diverged.NoOp.ShouldBeFalse();
            var conflict = diverged.FileMap.Single(d => d.Path == "f.txt");
            conflict.Kind.ShouldBe(FileDeltaKind.Conflict);
            conflict.MasterHash.ShouldBe(ContentHash.Compute("from master\n"u8.ToArray()));
            conflict.SlaveHash.ShouldBe(ContentHash.Compute("from slave\n"u8.ToArray()));
            diverged.Hint.DeSync.ShouldBeTrue();

            TestGitRepo.Commit(slave, "f.txt", "from master\n", "slave converges to master version");

            var resynced = Reconcile(Request(master, slave));

            resynced.DeSync.ShouldBeFalse();
            resynced.NoOp.ShouldBeTrue();
            resynced.FileMap.ShouldBeEmpty();
            resynced.Hint.DeSync.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(Directory.GetParent(master)!.FullName, true);
        }
    }

    [Fact]
    public void Reconcile_UnrelatedHistories_FullTwoWayMapping()
    {
        var master = TestGitRepo.Create();
        var slave = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(master, "a.txt", "A\n", "a only");
            TestGitRepo.Commit(master, "common.txt", "A-side\n", "common A");
            TestGitRepo.Commit(master, "equal.txt", "same\n", "equal A");
            TestGitRepo.Commit(slave, "b.txt", "B\n", "b only");
            TestGitRepo.Commit(slave, "common.txt", "B-side\n", "common B");
            TestGitRepo.Commit(slave, "equal.txt", "same\n", "equal B");

            var plan = Reconcile(Request(master, slave));

            plan.Relation.ShouldBe(HistoryRelation.Unrelated);
            plan.MergeBase.ShouldBeNull();
            plan.SlaveSyncHead.ShouldBeNull();
            plan.DeSync.ShouldBeFalse();
            plan.NoOp.ShouldBeFalse();

            plan.FileMap.Single(d => d.Path == "a.txt").Kind.ShouldBe(FileDeltaKind.MasterToSlave);
            plan.FileMap.Single(d => d.Path == "b.txt").Kind.ShouldBe(FileDeltaKind.SlaveToMaster);
            plan.FileMap.Single(d => d.Path == "common.txt").Kind.ShouldBe(FileDeltaKind.Conflict);
            plan.FileMap.ShouldNotContain(d => d.Path == "equal.txt");
            plan.FileMap.Count.ShouldBe(3);
        }
        finally
        {
            Directory.Delete(master, true);
            Directory.Delete(slave, true);
        }
    }

    [Fact]
    public void Reconcile_EqualContentAcrossDivergentIdentities_NoPhantomConflict()
    {
        var (master, slave, _) = CreatePair();
        try
        {
            TestGitRepo.Commit(master, "f.txt", "v2\n", "step one");
            TestGitRepo.Commit(master, "f.txt", "v3\n", "step two");
            TestGitRepo.Commit(slave, "f.txt", "v3\n", "single step to same tree");

            var plan = Reconcile(Request(master, slave));

            plan.DeSync.ShouldBeFalse();
            plan.NoOp.ShouldBeTrue();
            plan.FileMap.ShouldBeEmpty();
            plan.MasterOffers.Count.ShouldBe(2);
            plan.MasterOffers.ShouldAllBe(o => o.Disposition == OfferDisposition.Reoffer);
            plan.SlaveOffers.Count.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(Directory.GetParent(master)!.FullName, true);
        }
    }

    [Fact]
    public void Reconcile_SecondOfferOnSamePath_ReofferedAfterFirstApplied()
    {
        var (master, slave, _) = CreatePair();
        try
        {
            var firstSha = TestGitRepo.Commit(master, "f.txt", "v2\n", "first offer");
            var secondSha = TestGitRepo.Commit(master, "f.txt", "v3\n", "second offer");
            CommitOnSyncBranch(slave, "f.txt", "v2\n", "first offer applied verbatim");

            var plan = Reconcile(Request(master, slave));

            plan.MasterOffers.ShouldContain(o => o.Commit.Sha == firstSha && o.Disposition == OfferDisposition.AppliedCommitted);
            plan.MasterOffers.ShouldContain(o => o.Commit.Sha == secondSha && o.Disposition == OfferDisposition.Reoffer);
            plan.Hint.ConsumedShas.ShouldBe([firstSha]);
            plan.Hint.PendingShas.ShouldBe([secondSha]);
            plan.FileMap.Single(d => d.Path == "f.txt").Kind.ShouldBe(FileDeltaKind.MasterToSlave);
            plan.NoOp.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(Directory.GetParent(master)!.FullName, true);
        }
    }

    [Fact]
    public void Reconcile_SlaveEvolutionOfAcceptedFile_DoesNotStrandNextMasterOffer()
    {
        var (master, slave, _) = CreatePair();
        try
        {
            var offerSha = TestGitRepo.Commit(master, "f.txt", "v2\n", "master offer");
            TestGitRepo.Commit(master, "f.txt", "v4-master\n", "master moves on");
            GitCli.Run(slave, "checkout", "-B", "feature-sync");
            TestGitRepo.Commit(slave, "f.txt", "v2\n", "inbox applied verbatim");
            TestGitRepo.Commit(slave, "f.txt", "v3-slave\n", "slave evolves the accepted file");
            GitCli.Run(slave, "checkout", "main");

            var plan = Reconcile(Request(master, slave));

            plan.MasterOffers.ShouldContain(o => o.Commit.Sha == offerSha && o.Disposition == OfferDisposition.AppliedCommitted);
            var next = plan.MasterOffers.Single(o => o.Commit.Sha != offerSha);
            next.Disposition.ShouldBe(OfferDisposition.Reoffer);
            plan.FileMap.ShouldContain(d => d.Path == "f.txt" && d.Kind == FileDeltaKind.MasterToSlave);
            plan.Hint.PendingShas.ShouldBe([next.Commit.Sha]);
        }
        finally
        {
            Directory.Delete(Directory.GetParent(master)!.FullName, true);
        }
    }

    [Fact]
    public void Reconcile_CombinedInboxApplyThenEvolution_BothOffersConsumedAsModified()
    {
        var (master, slave, _) = CreatePair();
        try
        {
            var firstSha = TestGitRepo.Commit(master, "f.txt", "v2\n", "first offer");
            var secondSha = TestGitRepo.Commit(master, "f.txt", "v3\n", "second offer");
            GitCli.Run(slave, "checkout", "-B", "feature-sync");
            TestGitRepo.Commit(slave, "f.txt", "v3\n", "inbox applied in one commit");
            TestGitRepo.Commit(slave, "f.txt", "v3-slave\n", "slave evolves the file");
            GitCli.Run(slave, "checkout", "main");

            var plan = Reconcile(Request(master, slave));

            plan.MasterOffers.ShouldAllBe(o => o.Disposition == OfferDisposition.AppliedModified);
            plan.Hint.ConsumedShas.Count.ShouldBe(2);
            plan.Hint.ConsumedShas.ShouldContain(firstSha);
            plan.Hint.ConsumedShas.ShouldContain(secondSha);
            plan.FileMap.ShouldBeEmpty();
            plan.NoOp.ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(Directory.GetParent(master)!.FullName, true);
        }
    }

    [Fact]
    public void Reconcile_PartialInboxApply_UnappliedFileStaysReoffered()
    {
        var (master, slave, _) = CreatePair();
        try
        {
            TestGitRepo.Stage(master, "f.txt", "v2\n");
            TestGitRepo.Stage(master, "g.txt", "w2\n");
            GitCli.Run(master, "commit", "-m", "combined offer");
            var combinedSha = GitCli.Run(master, "rev-parse", "HEAD").Output.Trim();
            CommitOnSyncBranch(slave, "f.txt", "v2\n", "inbox applied only f.txt");

            var plan = Reconcile(Request(master, slave));

            plan.MasterOffers.Single().Commit.Sha.ShouldBe(combinedSha);
            plan.MasterOffers.Single().Disposition.ShouldBe(OfferDisposition.Reoffer);
            plan.FileMap.Select(d => d.Path).ShouldBe(["f.txt", "g.txt"]);
            plan.FileMap.ShouldAllBe(d => d.Kind == FileDeltaKind.MasterToSlave);
            plan.Hint.PendingShas.ShouldBe([combinedSha]);
            plan.NoOp.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(Directory.GetParent(master)!.FullName, true);
        }
    }

    [Fact]
    public void Reconcile_MissingSlaveBranch_FailsWithClearError()
    {
        var (master, slave, _) = CreatePair();
        try
        {
            var request = Request(master, slave) with { SlaveBaseBranch = "no-such-branch" };

            var result = SyncEngine.Reconcile(request);

            var fail = result.ShouldBeOfType<Result<SyncPlan>.Fail>();
            fail.Error.Message.ShouldContain("no-such-branch");
        }
        finally
        {
            Directory.Delete(Directory.GetParent(master)!.FullName, true);
        }
    }

    [Fact]
    public void LedgerHint_ToJson_RoundTripsShape()
    {
        var hint = new LedgerHint
        {
            Relation = HistoryRelation.Related,
            MergeBase = "b5c9d0",
            MasterHead = "aaa111",
            SlaveHead = "bbb222",
            SlaveSyncHead = null,
            ConsumedShas = new List<string> { "ccc333" },
            PendingShas = new List<string> { "ddd444" },
            DeSync = true
        };

        var json = LedgerHint.ToJson(hint);

        json.ShouldContain("\"relation\":\"related\"");
        json.ShouldContain("\"deSync\":true");
        var parsed = LedgerHint.Parse(json).ShouldNotBeNull();
        parsed.ShouldBeEquivalentTo(hint);
    }

    private static SyncPlan Reconcile(ReconcileRequest request) =>
        SyncEngine.Reconcile(request).ShouldBeOfType<Result<SyncPlan>.Ok>().Value;

    private static ReconcileRequest Request(string master, string slave) => new()
    {
        MasterRoot = master,
        MasterBaseBranch = "main",
        SlaveRoot = slave,
        SlaveBaseBranch = "main",
        SlaveSyncBranch = "feature-sync"
    };

    private static (string Master, string Slave, string BaseSha) CreatePair()
    {
        var root = TempDir.Create();
        var seed = Path.Combine(root, "seed");
        Directory.CreateDirectory(seed);
        GitCli.Init(seed);
        GitCli.Run(seed, "config", "user.name", "tester");
        GitCli.Run(seed, "config", "user.email", "tester@test.local");
        File.WriteAllText(Path.Combine(seed, "seed.txt"), "seed\n");
        GitCli.Run(seed, "add", ".");
        GitCli.Run(seed, "commit", "-m", "seed");
        var baseSha = GitCli.Run(seed, "rev-parse", "HEAD").Output.Trim();

        var master = Path.Combine(root, "master");
        var slave = Path.Combine(root, "slave");
        GitCli.Run(seed, "clone", "--quiet", seed, master);
        GitCli.Run(seed, "clone", "--quiet", seed, slave);
        foreach (var clone in new[] { master, slave })
        {
            GitCli.Run(clone, "config", "user.name", "tester");
            GitCli.Run(clone, "config", "user.email", "tester@test.local");
        }

        return (master, slave, baseSha);
    }

    private static void CommitOnSyncBranch(string repo, string path, string content, string message)
    {
        GitCli.Run(repo, "checkout", "-B", "feature-sync");
        TestGitRepo.Commit(repo, path, content, message);
        GitCli.Run(repo, "checkout", "main");
    }
}
