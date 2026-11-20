using Shouldly;
using Synchro.Core;
using Synchro.Core.Store;
using Synchro.Tests.Fixtures;
using Xunit;

namespace Synchro.Tests.E2E;

public class MasterStoreSelfHealE2ETests
{
    private static readonly DateTimeOffset AcceptDate = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PostWipeDate = new(2026, 5, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MasterStoreWiped_MidLife_ReinitAndNextSyncSelfHealsFromSlaveTruth()
    {
        var cooked = BothSidesNewCooker.Cook();
        try
        {
            var state = ScenarioPair.RegisterSlave(cooked.Master.Root, cooked.Slave.Root);
            var first = ScenarioPair.Sync(cooked.Master.Root, state);
            first.Report.ToSlave!.WrittenFiles.ShouldBe(["master-only.txt"]);
            first.Report.ToMaster!.WrittenFiles.ShouldBe(["slave-only.txt"]);

            cooked.Slave.CommitFiles(
                [new RepoFile("master-only.txt", "master side work\n")],
                "accept master inbox",
                AcceptDate);
            cooked.Slave.Checkout("main");
            cooked.Master.CommitFiles(
                [new RepoFile("slave-only.txt", "slave side work\n")],
                "accept slave inbox",
                AcceptDate);
            cooked.Master.Checkout("main");
            GitCli.StatusPorcelain(cooked.Master.Root).ShouldBeEmpty();
            GitCli.StatusPorcelain(cooked.Slave.Root).ShouldBeEmpty();

            Directory.Delete(Path.Combine(cooked.Master.Root, ".synchro"), true);
            var postWipeSha = cooked.Master.Commit(
                "post.txt", "post-wipe master work\n", "master works after wipe", PostWipeDate);

            var reborn = ScenarioPair.RegisterSlave(cooked.Master.Root, cooked.Slave.Root);
            SlaveHints.Get(reborn, ScenarioPair.SlaveName).ShouldBeOfType<Result<byte[]>.Fail>();

            var second = ScenarioPair.Sync(cooked.Master.Root, reborn);

            second.Report.ToSlave!.WrittenFiles.ShouldBe(["post.txt"]);
            second.Report.ToMaster.ShouldBeNull();
            second.Report.Conflicts.ShouldBeEmpty();
            second.Report.AlreadyCommitted.ShouldBe(["slave-only.txt"]);
            File.ReadAllText(Path.Combine(cooked.Slave.Root, "post.txt")).ShouldBe("post-wipe master work\n");
            File.ReadAllText(Path.Combine(cooked.Slave.Root, "master-only.txt")).ShouldBe("master side work\n");
            GitCli.Run(cooked.Slave.Root, "branch", "--show-current").Output.Trim().ShouldBe("feature-sync");
            GitCli.Run(cooked.Master.Root, "branch", "--show-current").Output.Trim().ShouldBe("main");

            var hint = ScenarioPair.Hint(second.State);
            hint.PendingShas.ShouldBe([postWipeSha]);
            hint.ConsumedShas.ShouldBe([cooked.MasterTipSha]);
        }
        finally
        {
            cooked.Master.Delete();
            cooked.Slave.Delete();
            cooked.Origin.Delete();
        }
    }
}
