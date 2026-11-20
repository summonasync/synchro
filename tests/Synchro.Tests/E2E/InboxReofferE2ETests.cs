using Shouldly;
using Synchro.Tests.Fixtures;
using Xunit;

namespace Synchro.Tests.E2E;

public class InboxReofferE2ETests
{
    private static readonly DateTimeOffset AcceptDate = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EvolveDate = new(2026, 5, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SlaveInboxDiscarded_MasterContentReOfferedNextSync()
    {
        var cooked = BothSidesNewCooker.Cook();
        try
        {
            var state = ScenarioPair.RegisterSlave(cooked.Master.Root, cooked.Slave.Root);
            var first = ScenarioPair.Sync(cooked.Master.Root, state);
            first.Report.ToSlave!.WrittenFiles.ShouldBe(["master-only.txt"]);
            first.Report.ToMaster!.WrittenFiles.ShouldBe(["slave-only.txt"]);

            cooked.Slave.Checkout("main");
            File.Delete(Path.Combine(cooked.Slave.Root, "master-only.txt"));
            GitCli.StatusPorcelain(cooked.Slave.Root).ShouldBeEmpty();
            cooked.Master.CommitFiles(
                [new RepoFile("slave-only.txt", "slave side work\n")],
                "accept slave inbox",
                AcceptDate);
            cooked.Master.Checkout("main");

            var second = ScenarioPair.Sync(cooked.Master.Root, first.State);

            second.Report.ToSlave!.WrittenFiles.ShouldBe(["master-only.txt"]);
            second.Report.ToMaster.ShouldBeNull();
            second.Report.AlreadyCommitted.ShouldBe(["slave-only.txt"]);
            File.ReadAllText(Path.Combine(cooked.Slave.Root, "master-only.txt")).ShouldBe("master side work\n");
            GitCli.Run(cooked.Slave.Root, "branch", "--show-current").Output.Trim().ShouldBe("feature-sync");

            var hint = ScenarioPair.Hint(second.State);
            hint.PendingShas.ShouldBe([cooked.MasterTipSha]);
            hint.ConsumedShas.ShouldBeEmpty();
        }
        finally
        {
            cooked.Master.Delete();
            cooked.Slave.Delete();
            cooked.Origin.Delete();
        }
    }

    [Fact]
    public void MasterInboxDiscarded_SlaveContentReOfferedNextSync()
    {
        var cooked = BothSidesNewCooker.Cook();
        try
        {
            var state = ScenarioPair.RegisterSlave(cooked.Master.Root, cooked.Slave.Root);
            var first = ScenarioPair.Sync(cooked.Master.Root, state);
            first.Report.ToSlave!.WrittenFiles.ShouldBe(["master-only.txt"]);
            first.Report.ToMaster!.WrittenFiles.ShouldBe(["slave-only.txt"]);

            cooked.Master.Checkout("main");
            File.Delete(Path.Combine(cooked.Master.Root, "slave-only.txt"));
            GitCli.StatusPorcelain(cooked.Master.Root).ShouldBeEmpty();
            cooked.Slave.CommitFiles(
                [new RepoFile("master-only.txt", "master side work\n")],
                "accept master inbox",
                AcceptDate);
            cooked.Slave.Checkout("main");

            var second = ScenarioPair.Sync(cooked.Master.Root, first.State);

            second.Report.ToMaster!.WrittenFiles.ShouldBe(["slave-only.txt"]);
            second.Report.ToSlave.ShouldBeNull();
            second.Report.AlreadyCommitted.ShouldBeEmpty();
            File.ReadAllText(Path.Combine(cooked.Master.Root, "slave-only.txt")).ShouldBe("slave side work\n");
            GitCli.Run(cooked.Master.Root, "branch", "--show-current").Output.Trim().ShouldBe("feature-sync");

            var hint = ScenarioPair.Hint(second.State);
            hint.PendingShas.ShouldBeEmpty();
            hint.ConsumedShas.ShouldBe([cooked.MasterTipSha]);
        }
        finally
        {
            cooked.Master.Delete();
            cooked.Slave.Delete();
            cooked.Origin.Delete();
        }
    }

    [Fact]
    public void AppliedThenModified_MasterOfferNotReOffered_EvolutionLandsAsSlaveWork()
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
            cooked.Slave.Commit(
                "master-only.txt",
                "master side work\nslave evolution\n",
                "slave evolves applied file",
                EvolveDate);
            cooked.Master.CommitFiles(
                [new RepoFile("slave-only.txt", "slave side work\n")],
                "accept slave inbox",
                AcceptDate);
            cooked.Master.Checkout("main");

            var second = ScenarioPair.Sync(cooked.Master.Root, first.State);

            second.Report.ToSlave.ShouldBeNull();
            second.Report.ToMaster!.WrittenFiles.ShouldBe(["master-only.txt"]);
            second.Report.AlreadyCommitted.ShouldBe(["slave-only.txt"]);
            File.ReadAllText(Path.Combine(cooked.Master.Root, "master-only.txt"))
                .ShouldBe("master side work\nslave evolution\n");
            GitCli.Run(cooked.Master.Root, "branch", "--show-current").Output.Trim().ShouldBe("feature-sync");

            var hint = ScenarioPair.Hint(second.State);
            hint.PendingShas.ShouldBeEmpty();
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
