using Shouldly;
using Synchro.Core;
using Synchro.Core.Slave;
using Synchro.Tests.Fixtures;
using Xunit;

namespace Synchro.Tests.E2E;

public class SyncRefusalMatrixE2ETests
{
    private static readonly DateTimeOffset SeedDate = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DirtyTree_OnMasterBase_RefusesBeforeAnyLanding()
    {
        var error = DirtyTreeCell();

        error.Code.ShouldBe("conflict");
        error.Message.ShouldContain("uncommitted changes");
    }

    [Fact]
    public void WrongBaseBranch_OnSlave_RefusesWithBranchNames()
    {
        var error = WrongBaseBranchCell();

        error.Code.ShouldBe("conflict");
        error.Message.ShouldContain("receiving repo is on 'experiment'");
        error.Message.ShouldContain("'main'");
    }

    [Fact]
    public void OpenInbox_AfterLanding_RefusesResync()
    {
        var error = OpenInboxCell();

        error.Code.ShouldBe("conflict");
        error.Message.ShouldContain("inbox on 'feature-sync' is still open");
        error.Message.ShouldContain("rollback");
    }

    [Fact]
    public void NestedPath_AtAdd_Refuses()
    {
        var error = NestedPathCell();

        error.Code.ShouldBe("conflict");
        error.Message.ShouldContain("nested inside the master working tree");
    }

    [Fact]
    public void SameRepoPath_AtAdd_Refuses()
    {
        var error = SameRepoPathCell();

        error.Code.ShouldBe("conflict");
        error.Message.ShouldContain("is the master repo itself");
    }

    [Fact]
    public void WorktreeLinkedPath_AtAdd_Refuses()
    {
        var error = WorktreeLinkedCell();

        error.Code.ShouldBe("conflict");
        error.Message.ShouldContain("git worktree linked to another repo");
    }

    [Fact]
    public void RefusalMatrix_AllSixRefusalsCarryDistinctMessages()
    {
        var messages = new[]
        {
            DirtyTreeCell().Message,
            WrongBaseBranchCell().Message,
            OpenInboxCell().Message,
            NestedPathCell().Message,
            SameRepoPathCell().Message,
            WorktreeLinkedCell().Message
        };

        messages.Distinct().Count().ShouldBe(6);
    }

    private static SyncError DirtyTreeCell()
    {
        var area = SeededArea();
        try
        {
            var state = ScenarioPair.RegisterSlave(area.Master.Root, area.Slave.Root);
            File.WriteAllText(Path.Combine(area.Master.Root, "loose.txt"), "uncommitted human work");

            var error = ScenarioPair.SyncFails(area.Master.Root, state);

            GitCli.Run(area.Slave.Root, "branch", "--show-current").Output.Trim().ShouldBe("main");
            GitCli.Run(area.Slave.Root, "branch", "--list", "feature-sync").Output.Trim().ShouldBeEmpty();
            return error;
        }
        finally
        {
            area.Dispose();
        }
    }

    private static SyncError WrongBaseBranchCell()
    {
        var area = SeededArea();
        try
        {
            var state = ScenarioPair.RegisterSlave(area.Master.Root, area.Slave.Root);
            area.Slave.Branch("experiment");
            area.Slave.Checkout("experiment");

            return ScenarioPair.SyncFails(area.Master.Root, state);
        }
        finally
        {
            area.Dispose();
        }
    }

    private static SyncError OpenInboxCell()
    {
        var area = SeededArea();
        try
        {
            var state = ScenarioPair.RegisterSlave(area.Master.Root, area.Slave.Root);
            ScenarioPair.Sync(area.Master.Root, state);
            GitCli.Run(area.Master.Root, "branch", "--show-current").Output.Trim().ShouldBe("feature-sync");

            return ScenarioPair.SyncFails(area.Master.Root, state);
        }
        finally
        {
            area.Dispose();
        }
    }

    private static SyncError NestedPathCell()
    {
        var area = SeededArea();
        try
        {
            var nested = Directory.CreateDirectory(
                Path.Combine(area.Master.Root, "inner", "deep")).FullName;

            return AddSlaveService.Run(
                    area.Master.Root, ScenarioPair.EmptyState(), nested, new ScriptedAddSlaveUi())
                .ShouldBeOfType<AddSlaveResult.Refused>().Error;
        }
        finally
        {
            area.Dispose();
        }
    }

    private static SyncError SameRepoPathCell()
    {
        var area = SeededArea();
        try
        {
            return AddSlaveService.Run(
                    area.Master.Root, ScenarioPair.EmptyState(), area.Master.Root, new ScriptedAddSlaveUi())
                .ShouldBeOfType<AddSlaveResult.Refused>().Error;
        }
        finally
        {
            area.Dispose();
        }
    }

    private static SyncError WorktreeLinkedCell()
    {
        var area = SeededArea();
        var worktree = TempDir.Create();
        try
        {
            FixtureGit.Output(area.Master.Root, "worktree", "add", worktree, "-b", "wt-topic");

            return AddSlaveService.Run(
                    area.Master.Root, ScenarioPair.EmptyState(), worktree, new ScriptedAddSlaveUi())
                .ShouldBeOfType<AddSlaveResult.Refused>().Error;
        }
        finally
        {
            Directory.Delete(worktree, true);
            area.Dispose();
        }
    }

    private static SeededAreaRecord SeededArea()
    {
        var master = TempRepoBuilder.Create();
        var slave = TempRepoBuilder.Create();
        master.Commit("m.txt", "master seed\n", "master seed", SeedDate);
        slave.Commit("s.txt", "slave seed\n", "slave seed", SeedDate);
        return new SeededAreaRecord(master, slave);
    }

    private sealed record SeededAreaRecord(TempRepoBuilder Master, TempRepoBuilder Slave) : IDisposable
    {
        public void Dispose()
        {
            Master.Delete();
            Slave.Delete();
        }
    }
}
