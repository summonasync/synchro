using Shouldly;
using Synchro.Core.Store;
using Synchro.Core.Sync;
using Synchro.Tests.Fixtures;
using Xunit;

namespace Synchro.Tests.E2E;

public class UnrelatedHistoriesFirstSyncE2ETests
{
    private static readonly DateTimeOffset MasterNoteDate = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SlaveNoteDate = new(2026, 5, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AcceptDate = new(2026, 5, 3, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UnrelatedRoots_FirstSyncLandsContentDeltasBothWays_AcceptedInboxesMakeSecondSyncNoOp()
    {
        var cooked = UnrelatedHistoriesCooker.Cook();
        try
        {
            cooked.Master.Commit(
                "docs/master-note.md", "master adds notes\n", "master adds notes", MasterNoteDate);
            cooked.Slave.Commit("docs/slave-note.md", "slave adds notes\n", "slave adds notes", SlaveNoteDate);

            var state = ScenarioPair.RegisterSlave(cooked.Master.Root, cooked.Slave.Root);
            var first = ScenarioPair.Sync(cooked.Master.Root, state);

            first.Report.Conflicts.ShouldBeEmpty();
            first.Report.AlreadyCommitted.ShouldBeEmpty();
            first.Report.ToSlave!.WrittenFiles.ShouldBe(["docs/master-note.md", "master.txt"]);
            first.Report.ToMaster!.WrittenFiles.ShouldBe(["docs/slave-note.md", "slave.txt"]);
            File.ReadAllText(Path.Combine(cooked.Slave.Root, "docs/master-note.md")).ShouldBe("master adds notes\n");
            File.ReadAllText(Path.Combine(cooked.Slave.Root, "master.txt")).ShouldBe("master root content\n");
            File.ReadAllText(Path.Combine(cooked.Master.Root, "docs/slave-note.md")).ShouldBe("slave adds notes\n");
            File.ReadAllText(Path.Combine(cooked.Master.Root, "slave.txt")).ShouldBe("slave root content\n");
            GitCli.Run(cooked.Slave.Root, "branch", "--show-current").Output.Trim().ShouldBe("feature-sync");
            GitCli.Run(cooked.Master.Root, "branch", "--show-current").Output.Trim().ShouldBe("feature-sync");
            GitCli.StatusPorcelain(cooked.Slave.Root).ShouldNotBeEmpty();
            GitCli.StatusPorcelain(cooked.Master.Root).ShouldNotBeEmpty();

            var firstHint = ScenarioPair.Hint(first.State);
            firstHint.Relation.ShouldBe(HistoryRelation.Unrelated);
            firstHint.MergeBase.ShouldBeNull();
            firstHint.DeSync.ShouldBeFalse();
            firstHint.PendingShas.ShouldBeEmpty();
            firstHint.ConsumedShas.ShouldBeEmpty();

            cooked.Slave.Checkout("main");
            cooked.Slave.CommitFiles(
                [new RepoFile("docs/master-note.md", "master adds notes\n"), new RepoFile("master.txt", "master root content\n")],
                "absorb master content",
                AcceptDate);
            cooked.Master.Checkout("main");
            cooked.Master.CommitFiles(
                [new RepoFile("docs/slave-note.md", "slave adds notes\n"), new RepoFile("slave.txt", "slave root content\n")],
                "absorb slave content",
                AcceptDate);

            var second = ScenarioPair.Sync(cooked.Master.Root, first.State);

            second.Report.ToSlave.ShouldBeNull();
            second.Report.ToMaster.ShouldBeNull();
            second.Report.Conflicts.ShouldBeEmpty();
            second.Report.AlreadyCommitted.ShouldBeEmpty();
            GitCli.Run(cooked.Slave.Root, "branch", "--show-current").Output.Trim().ShouldBe("main");
            GitCli.Run(cooked.Master.Root, "branch", "--show-current").Output.Trim().ShouldBe("main");
            GitCli.StatusPorcelain(cooked.Slave.Root).ShouldBeEmpty();
            GitCli.StatusPorcelain(cooked.Master.Root).ShouldBeEmpty();

            var secondHint = ScenarioPair.Hint(second.State);
            secondHint.Relation.ShouldBe(HistoryRelation.Unrelated);
            secondHint.PendingShas.ShouldBeEmpty();
            secondHint.ConsumedShas.ShouldBeEmpty();

            var history = ScenarioPair.History(second.State);
            history.Count.ShouldBe(4);
            history[0].Direction.ShouldBe(SyncDirection.SlaveToMaster);
            history[0].FilesChanged.ShouldBe(0);
            history[1].Direction.ShouldBe(SyncDirection.MasterToSlave);
            history[1].FilesChanged.ShouldBe(0);
            history[2].FilesChanged.ShouldBe(2);
            history[3].FilesChanged.ShouldBe(2);
        }
        finally
        {
            cooked.Master.Delete();
            cooked.Slave.Delete();
        }
    }
}
