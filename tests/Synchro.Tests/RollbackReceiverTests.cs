using Shouldly;
using Synchro.Core;
using Synchro.Core.Landing;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class RollbackReceiverTests
{
    private static SlaveConfig Slave(string root) => new()
    {
        Name = "laptop",
        Root = root,
        SyncBranch = "feature-sync",
        BaseBranch = "main",
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private static string Resolve(string masterRoot, string slaveRoot, bool preferMaster = false) =>
        RollbackReceiver.Resolve(masterRoot, Slave(slaveRoot), preferMaster)
            .ShouldBeOfType<Result<string>.Ok>().Value;

    private static string ResolveFails(string masterRoot, string slaveRoot, bool preferMaster = false) =>
        RollbackReceiver.Resolve(masterRoot, Slave(slaveRoot), preferMaster)
            .ShouldBeOfType<Result<string>.Fail>().Error.Message;

    private static string CurrentBranch(string repo) =>
        GitCli.Run(repo, "branch", "--show-current").Output.Trim();

    [Fact]
    public void Resolve_MasterParkedOnSyncBranchNameWithSlavePresent_RefusesAndLeavesMasterTreeAlone()
    {
        var master = TestGitRepo.Create();
        var slaveRoot = Path.Combine(TempDir.Create(), "slave-root");
        try
        {
            TestGitRepo.Commit(master, "master-file.txt", "unrelated master work\n", "seed");
            GitCli.Run(master, "checkout", "-b", "feature-sync");
            File.WriteAllText(Path.Combine(master, "master-file.txt"), "edited by the master dev\n");
            File.WriteAllText(Path.Combine(master, "untracked.txt"), "precious\n");
            Directory.CreateDirectory(slaveRoot);

            var message = ResolveFails(master, slaveRoot);

            message.ShouldContain(master);
            message.ShouldContain(slaveRoot);
            message.ShouldContain("--master");

            CurrentBranch(master).ShouldBe("feature-sync");
            File.ReadAllText(Path.Combine(master, "master-file.txt")).ShouldBe("edited by the master dev\n");
            File.Exists(Path.Combine(master, "untracked.txt")).ShouldBeTrue();
            GitCli.StatusPorcelain(master).ShouldContain(" M master-file.txt");
        }
        finally
        {
            Directory.Delete(master, true);
            Directory.Delete(Path.GetDirectoryName(slaveRoot)!, true);
        }
    }

    [Fact]
    public void Resolve_MasterOnSyncBranchAndSlaveFolderMissing_TakesMasterRoot()
    {
        var master = TestGitRepo.Create();
        var missingSlave = Path.Combine(TempDir.Create(), "never-created");
        try
        {
            TestGitRepo.Commit(master, "seed.txt", "seed\n", "seed");
            GitCli.Run(master, "checkout", "-b", "feature-sync");

            Resolve(master, missingSlave).ShouldBe(master);
        }
        finally
        {
            Directory.Delete(master, true);
            Directory.Delete(Path.GetDirectoryName(missingSlave)!, true);
        }
    }

    [Fact]
    public void Resolve_MasterOffSyncBranch_TakesSlaveRoot()
    {
        var master = TestGitRepo.Create();
        var slaveRoot = TempDir.Create();
        try
        {
            TestGitRepo.Commit(master, "seed.txt", "seed\n", "seed");
            GitCli.Run(master, "checkout", "-b", "feature-sync");
            GitCli.Run(master, "checkout", "main");

            Resolve(master, slaveRoot).ShouldBe(slaveRoot);
        }
        finally
        {
            Directory.Delete(master, true);
            Directory.Delete(slaveRoot, true);
        }
    }

    [Fact]
    public void Resolve_ExplicitMasterWhileOnSyncBranch_TakesMasterRoot()
    {
        var master = TestGitRepo.Create();
        var slaveRoot = TempDir.Create();
        try
        {
            TestGitRepo.Commit(master, "seed.txt", "seed\n", "seed");
            GitCli.Run(master, "checkout", "-b", "feature-sync");

            Resolve(master, slaveRoot, preferMaster: true).ShouldBe(master);
        }
        finally
        {
            Directory.Delete(master, true);
            Directory.Delete(slaveRoot, true);
        }
    }

    [Fact]
    public void Resolve_ExplicitMasterWhileOffSyncBranch_Refuses()
    {
        var master = TestGitRepo.Create();
        var slaveRoot = TempDir.Create();
        try
        {
            TestGitRepo.Commit(master, "seed.txt", "seed\n", "seed");

            ResolveFails(master, slaveRoot, preferMaster: true).ShouldContain("no inbox to roll back");
        }
        finally
        {
            Directory.Delete(master, true);
            Directory.Delete(slaveRoot, true);
        }
    }
}
