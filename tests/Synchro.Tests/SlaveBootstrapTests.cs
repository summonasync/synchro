using Shouldly;
using Synchro.Core;
using Synchro.Core.Slave;
using Xunit;

namespace Synchro.Tests;

public class SlaveBootstrapTests
{
    [Fact]
    public void Init_PlainFolder_MakesExactlyOneNeutralCommitAndKeepsFolderEmpty()
    {
        var dir = TempDir.Create();
        try
        {
            File.WriteAllText(Path.Combine(dir, "note.txt"), "existing note\n");

            SlaveBootstrap.Init(dir, "main").ShouldBeNull();

            GitCli.Run(dir, "rev-list", "--count", "HEAD").Output.Trim().ShouldBe("1");
            var log = GitCli.Run(dir, "log", "-1", "--pretty=%an%n%ae%n%s").Output;
            log.ShouldContain("synchro");
            log.ShouldContain("synchro@localhost");
            log.ShouldContain("synchro bootstrap");
            GitCli.StatusPorcelain(dir).ShouldBe("?? note.txt\n");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Init_InvalidBaseBranch_FailsAndLeavesNoCommit()
    {
        var dir = TempDir.Create();
        try
        {
            var error = SlaveBootstrap.Init(dir, "bad..name")
                .ShouldBeOfType<SyncError>();

            error.Message.ShouldContain("git init failed");
            GitCli.Run(dir, "rev-list", "--count", "HEAD").ExitCode.ShouldNotBe(0);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Init_BaseBranchName_BecomesTheCheckedOutBranch()
    {
        var dir = TempDir.Create();
        try
        {
            SlaveBootstrap.Init(dir, "trunk").ShouldBeNull();

            GitCli.Run(dir, "branch", "--show-current").Output.Trim().ShouldBe("trunk");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
