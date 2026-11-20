using System.Security.Cryptography;
using System.Text;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Landing;
using Synchro.Core.Store;
using Synchro.Core.Sync;
using Xunit;

namespace Synchro.Tests;

public class SyncOrchestratorTests
{
    [Fact]
    public void Sync_TwoWayNonConflictingChanges_LandsDirtyInboxesOnBothSides()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            var masterOffer = TestGitRepo.Commit(master, "feature-m.txt", "from master\n", "master adds feature");
            TestGitRepo.Commit(slave, "feature-s.txt", "from slave\n", "slave adds feature");

            var output = TestSyncPair.Sync(master, state);
            var report = output.Report;

            report.Conflicts.ShouldBeEmpty();
            report.AlreadyCommitted.ShouldBeEmpty();
            report.ToSlave.ShouldNotBeNull();
            report.ToSlave!.WrittenFiles.ShouldBe(["feature-m.txt"]);
            report.ToMaster.ShouldNotBeNull();
            report.ToMaster!.WrittenFiles.ShouldBe(["feature-s.txt"]);

            GitCli.Run(slave, "branch", "--show-current").Output.Trim().ShouldBe("feature-sync");
            File.ReadAllText(Path.Combine(slave, "feature-m.txt")).ShouldBe("from master\n");
            GitCli.StatusPorcelain(slave).ShouldNotBe("");

            GitCli.Run(master, "branch", "--show-current").Output.Trim().ShouldBe("feature-sync");
            File.ReadAllText(Path.Combine(master, "feature-s.txt")).ShouldBe("from slave\n");
            GitCli.StatusPorcelain(master).ShouldNotBe("");

            InFlightMarker.Exists(InFlightMarker.PathFor(master, "laptop")).ShouldBeFalse();

            var hint = HintOf(output.State);
            hint.PendingShas.ShouldBe([masterOffer]);
            hint.ConsumedShas.ShouldBeEmpty();

            var history = HistoryOf(output.State);
            history.Count.ShouldBe(2);
            history[0].Direction.ShouldBe(SyncDirection.SlaveToMaster);
            history[0].FilesChanged.ShouldBe(1);
            history[0].Conflicts.ShouldBeEmpty();
            history[1].Direction.ShouldBe(SyncDirection.MasterToSlave);
            history[1].FilesChanged.ShouldBe(1);
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }

    [Fact]
    public void Sync_NonInteractiveConflict_DefaultsMasterWinsAndFlagsIt()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            TestGitRepo.Commit(master, "shared.txt", "master version\n", "master rewrites shared");
            TestGitRepo.Commit(slave, "shared.txt", "slave version\n", "slave rewrites shared");

            var output = TestSyncPair.Sync(master, state, nonInteractive: true);

            output.Report.NonInteractive.ShouldBeTrue();
            var conflict = output.Report.Conflicts.Single();
            conflict.Path.ShouldBe("shared.txt");
            conflict.Winner.ShouldBe(ConflictWinner.Master);

            output.Report.ToSlave.ShouldNotBeNull();
            output.Report.ToSlave!.WrittenFiles.ShouldBe(["shared.txt"]);
            output.Report.ToMaster.ShouldBeNull();

            File.ReadAllText(Path.Combine(slave, "shared.txt")).ShouldBe("master version\n");
            GitCli.Run(master, "branch", "--show-current").Output.Trim().ShouldBe("main");
            GitCli.StatusPorcelain(master).ShouldBe("");
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }

    [Fact]
    public void Sync_InteractiveConflicts_HonorBothChoices()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            TestGitRepo.Commit(master, "a.txt", "a-master\n", "master first");
            TestGitRepo.Commit(master, "b.txt", "b-master\n", "master second");
            TestGitRepo.Commit(slave, "a.txt", "a-slave\n", "slave first");
            TestGitRepo.Commit(slave, "b.txt", "b-slave\n", "slave second");

            var ui = new ScriptedSyncUi(new Dictionary<string, ConflictWinner>
            {
                ["a.txt"] = ConflictWinner.Master,
                ["b.txt"] = ConflictWinner.Slave
            });

            var output = TestSyncPair.Sync(master, state, ui, nonInteractive: false);

            output.Report.Conflicts.Count.ShouldBe(2);
            output.Report.Conflicts[0].ShouldBe(new SyncConflict { Path = "a.txt", Winner = ConflictWinner.Master });
            output.Report.Conflicts[1].ShouldBe(new SyncConflict { Path = "b.txt", Winner = ConflictWinner.Slave });

            output.Report.ToSlave!.WrittenFiles.ShouldBe(["a.txt"]);
            File.ReadAllText(Path.Combine(slave, "a.txt")).ShouldBe("a-master\n");
            File.ReadAllText(Path.Combine(slave, "b.txt")).ShouldBe("b-slave\n");

            output.Report.ToMaster!.WrittenFiles.ShouldBe(["b.txt"]);
            File.ReadAllText(Path.Combine(master, "b.txt")).ShouldBe("b-slave\n");
            File.ReadAllText(Path.Combine(master, "a.txt")).ShouldBe("a-master\n");

            var history = HistoryOf(output.State);
            history.Single(r => r.Direction == SyncDirection.MasterToSlave).Conflicts
                .ShouldBe([new SyncConflict { Path = "a.txt", Winner = ConflictWinner.Master }]);
            history.Single(r => r.Direction == SyncDirection.SlaveToMaster).Conflicts
                .ShouldBe([new SyncConflict { Path = "b.txt", Winner = ConflictWinner.Slave }]);
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }

    [Fact]
    public void Sync_SlaveInboxStillOpen_RefusesBeforeTouchingAnything()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            TestGitRepo.Commit(master, "feature-m.txt", "from master\n", "master adds feature");
            TestSyncPair.Sync(master, state);

            var error = TestSyncPair.SyncFails(master, state);

            error.Message.ShouldContain("inbox");
            error.Message.ShouldContain("feature-sync");
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }

    [Fact]
    public void Sync_MasterDirtyBaseTree_Refuses()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            TestGitRepo.Commit(slave, "feature-s.txt", "from slave\n", "slave adds feature");
            File.WriteAllText(Path.Combine(master, "local.txt"), "human work");

            var error = TestSyncPair.SyncFails(master, state);

            error.Message.ShouldContain("uncommitted changes");
            GitCli.Run(slave, "branch", "--show-current").Output.Trim().ShouldBe("main");
            GitCli.StatusPorcelain(slave).ShouldBe("");
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }

    [Fact]
    public void Sync_MasterParkedOnSyncBranch_RefusesAsOpenInbox()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            TestGitRepo.Commit(slave, "feature-s.txt", "from slave\n", "slave adds feature");
            TestSyncPair.Sync(master, state);
            GitCli.Run(master, "branch", "--show-current").Output.Trim().ShouldBe("feature-sync");

            var error = TestSyncPair.SyncFails(master, state);

            error.Message.ShouldContain("inbox");
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }

    [Fact]
    public void Sync_UnrelatedHistories_SyncsByContentBothWays()
    {
        var master = TestGitRepo.Create();
        var slave = TestGitRepo.Create();
        try
        {
            var (_, state) = TestSyncPair.InitStoreWithSlave(master, slave);
            TestGitRepo.Commit(master, "a.txt", "A\n", "master a");
            TestGitRepo.Commit(slave, "b.txt", "B\n", "slave b");

            var output = TestSyncPair.Sync(master, state);

            output.Report.ToSlave!.WrittenFiles.ShouldBe(["a.txt"]);
            output.Report.ToMaster!.WrittenFiles.ShouldBe(["b.txt"]);
            HintOf(output.State).Relation.ShouldBe(HistoryRelation.Unrelated);
        }
        finally
        {
            Directory.Delete(master, true);
            Directory.Delete(slave, true);
        }
    }

    [Fact]
    public void Sync_CommittedInboxThenMasterBaseEdit_ConflictFiresAndMasterContentLands()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            TestGitRepo.Commit(slave, "s.txt", "slave content\n", "slave adds s");

            var first = TestSyncPair.Sync(master, state);
            first.Report.ToMaster!.WrittenFiles.ShouldBe(["s.txt"]);

            TestGitRepo.Commit(master, "s.txt", "slave content\n", "accept slave inbox");
            GitCli.Run(master, "checkout", "main");
            TestGitRepo.Commit(master, "s.txt", "master edit\n", "master edits on base");

            var output = TestSyncPair.Sync(master, first.State);

            var conflict = output.Report.Conflicts.Single();
            conflict.Path.ShouldBe("s.txt");
            conflict.Winner.ShouldBe(ConflictWinner.Master);
            output.Report.AlreadyCommitted.ShouldBeEmpty();
            output.Report.ToSlave.ShouldNotBeNull();
            output.Report.ToSlave!.WrittenFiles.ShouldBe(["s.txt"]);
            output.Report.ToMaster.ShouldBeNull();
            File.ReadAllText(Path.Combine(slave, "s.txt")).ShouldBe("master edit\n");
            GitCli.Run(master, "branch", "--show-current").Output.Trim().ShouldBe("main");
            GitCli.StatusPorcelain(master).ShouldBe("");
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }

    [Fact]
    public void Sync_MasterInboxDiscarded_SlaveContentReOffered()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            TestGitRepo.Commit(slave, "feature-s.txt", "from slave\n", "slave adds feature");

            var first = TestSyncPair.Sync(master, state);
            first.Report.ToMaster!.WrittenFiles.ShouldBe(["feature-s.txt"]);

            GitCli.Run(master, "checkout", "main");
            File.Delete(Path.Combine(master, "feature-s.txt"));
            GitCli.StatusPorcelain(master).ShouldBe("");

            var second = TestSyncPair.Sync(master, first.State);

            second.Report.AlreadyCommitted.ShouldBeEmpty();
            second.Report.Conflicts.ShouldBeEmpty();
            second.Report.ToMaster.ShouldNotBeNull();
            second.Report.ToMaster!.WrittenFiles.ShouldBe(["feature-s.txt"]);
            File.ReadAllText(Path.Combine(master, "feature-s.txt")).ShouldBe("from slave\n");
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }

    [Fact]
    public void Sync_SlaveEvolvesAfterInboxAccepted_NewContentReOffered()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            TestGitRepo.Commit(slave, "s.txt", "v1\n", "slave adds s");

            var first = TestSyncPair.Sync(master, state);
            first.Report.ToMaster!.WrittenFiles.ShouldBe(["s.txt"]);

            TestGitRepo.Commit(master, "s.txt", "v1\n", "accept slave inbox");
            GitCli.Run(master, "checkout", "main");
            TestGitRepo.Commit(slave, "s.txt", "v2\n", "slave evolves s");

            var second = TestSyncPair.Sync(master, first.State);

            second.Report.AlreadyCommitted.ShouldBeEmpty();
            second.Report.Conflicts.ShouldBeEmpty();
            second.Report.ToMaster.ShouldNotBeNull();
            second.Report.ToMaster!.WrittenFiles.ShouldBe(["s.txt"]);
            File.ReadAllText(Path.Combine(master, "s.txt")).ShouldBe("v2\n");
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }

    [Fact]
    public void Sync_MissingSlaveFolder_RefusesWithClearError()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            Directory.Delete(slave, true);

            var error = TestSyncPair.SyncFails(master, state);

            error.Message.ShouldContain("slave repo missing");
            error.Message.ShouldContain(slave);
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }

    private static LedgerHint HintOf(StoreState state) =>
        LedgerHint.Parse(Encoding.UTF8.GetString(
            SlaveHints.Get(state, "laptop").ShouldBeOfType<Result<byte[]>.Ok>().Value))
            .ShouldNotBeNull();

    private static IReadOnlyList<SyncHistoryRecord> HistoryOf(StoreState state) =>
        SyncHistoryLog.List(state, "laptop").ShouldBeOfType<Result<IReadOnlyList<SyncHistoryRecord>>.Ok>().Value;
}
