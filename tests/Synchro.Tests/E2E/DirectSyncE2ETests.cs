using System.Text;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Store;
using Synchro.Core.Sync;
using Xunit;

namespace Synchro.Tests.E2E;

public class DirectSyncE2ETests
{
    [Fact]
    public void RelatedHistories_RoundTrip_LandsBothInboxesAndSecondSyncIsNoOp()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            var masterOffer = TestGitRepo.Commit(master, "feature-m.txt", "master feature\n", "master adds feature");
            TestGitRepo.Commit(slave, "feature-s.txt", "slave feature\n", "slave adds feature");

            var first = TestSyncPair.Sync(master, state);

            first.Report.Conflicts.ShouldBeEmpty();
            first.Report.ToSlave!.WrittenFiles.ShouldBe(["feature-m.txt"]);
            first.Report.ToMaster!.WrittenFiles.ShouldBe(["feature-s.txt"]);
            GitCli.Run(slave, "branch", "--show-current").Output.Trim().ShouldBe("feature-sync");
            GitCli.Run(master, "branch", "--show-current").Output.Trim().ShouldBe("feature-sync");
            File.ReadAllText(Path.Combine(slave, "feature-m.txt")).ShouldBe("master feature\n");
            File.ReadAllText(Path.Combine(master, "feature-s.txt")).ShouldBe("slave feature\n");

            TestGitRepo.Commit(slave, "feature-m.txt", "master feature\n", "accept master inbox");
            GitCli.Run(slave, "checkout", "main");
            TestGitRepo.Commit(master, "feature-s.txt", "slave feature\n", "accept slave inbox");
            GitCli.Run(master, "checkout", "main");

            var second = TestSyncPair.Sync(master, first.State);

            second.Report.ToSlave.ShouldBeNull();
            second.Report.ToMaster.ShouldBeNull();
            second.Report.Conflicts.ShouldBeEmpty();
            second.Report.AlreadyCommitted.ShouldBe(["feature-s.txt"]);
            GitCli.StatusPorcelain(slave).ShouldBe("");
            GitCli.StatusPorcelain(master).ShouldBe("");
            GitCli.Run(slave, "branch", "--show-current").Output.Trim().ShouldBe("main");
            GitCli.Run(master, "branch", "--show-current").Output.Trim().ShouldBe("main");
            GitCli.Run(slave, "show", "feature-sync:feature-m.txt").Output.ShouldBe("master feature\n");
            GitCli.Run(master, "show", "feature-sync:feature-s.txt").Output.ShouldBe("slave feature\n");

            var hint = LedgerHint.Parse(Encoding.UTF8.GetString(
                SlaveHints.Get(second.State, "laptop").ShouldBeOfType<Result<byte[]>.Ok>().Value));
            hint.ShouldNotBeNull();
            hint!.ConsumedShas.ShouldBe([masterOffer]);
            hint.PendingShas.ShouldBeEmpty();
            hint.DeSync.ShouldBeFalse();

            var history = SyncHistoryLog.List(second.State, "laptop")
                .ShouldBeOfType<Result<IReadOnlyList<SyncHistoryRecord>>.Ok>().Value;
            history.Count.ShouldBe(4);
            history[0].FilesChanged.ShouldBe(0);
            history[1].FilesChanged.ShouldBe(0);
            history[2].FilesChanged.ShouldBe(1);
            history[3].FilesChanged.ShouldBe(1);
            GitCli.StatusPorcelain(master).ShouldNotContain(".synchro");
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }
}
