using Shouldly;
using Synchro.Core;
using Synchro.Core.Landing;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class LandingServiceTests
{
    private static SlaveConfig Slave(string root) => new()
    {
        Name = "laptop",
        Root = root,
        SyncBranch = "feature-sync",
        BaseBranch = "main",
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private static LandingReport Land(string repo, params LandingChange[] changes) =>
        LandingService.Land(repo, repo, Slave(repo), changes)
            .ShouldBeOfType<Result<LandingReport>.Ok>().Value;

    private static string LandFails(string repo, params LandingChange[] changes) =>
        LandingService.Land(repo, repo, Slave(repo), changes)
            .ShouldBeOfType<Result<LandingReport>.Fail>().Error.Message;

    private static string CurrentBranch(string repo) =>
        GitCli.Run(repo, "branch", "--show-current").Output.Trim();

    private static string CommitCount(string repo) =>
        GitCli.Run(repo, "rev-list", "--count", "HEAD").Output.Trim();

    private static string MarkerPath(string repo) => InFlightMarker.PathFor(repo, "laptop");

    private static string Repo()
    {
        var repo = TestGitRepo.Create();
        GitHiding.EnsureExcluded(repo);
        return repo;
    }

    [Fact]
    public void Land_CreatesSyncBranchFromBase_LeavesDirtyOnly()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "keep.txt", "keep\n", "seed");
            TestGitRepo.Commit(repo, "gone.txt", "gone\n", "add gone");
            var baseHead = GitCli.Run(repo, "rev-parse", "HEAD").Output.Trim();

            var report = Land(repo,
                LandingChange.WriteFile("new/dir/file.txt", "fresh\r\ncontent\r\n"),
                new LandingChange.Write("keep.txt", "keep\nv2"u8.ToArray()),
                new LandingChange.Delete("gone.txt"));

            report.BranchCreated.ShouldBeTrue();
            report.Branch.ShouldBe("feature-sync");
            report.WrittenFiles.ShouldBe(new[] { "new/dir/file.txt", "keep.txt" });
            report.DeletedFiles.ShouldBe(["gone.txt"]);
            CurrentBranch(repo).ShouldBe("feature-sync");

            File.ReadAllText(Path.Combine(repo, "new", "dir", "file.txt")).ShouldBe("fresh\r\ncontent\r\n");
            File.ReadAllText(Path.Combine(repo, "keep.txt")).ShouldBe("keep\nv2");
            File.Exists(Path.Combine(repo, "gone.txt")).ShouldBeFalse();

            var status = GitCli.StatusPorcelain(repo);
            status.ShouldContain("?? new/");
            status.ShouldContain(" M keep.txt");
            status.ShouldContain(" D gone.txt");

            CommitCount(repo).ShouldBe("2");
            GitCli.Run(repo, "rev-parse", "HEAD").Output.Trim().ShouldBe(baseHead);
            InFlightMarker.Exists(MarkerPath(repo)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Land_ReusesExistingSyncBranch()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            GitCli.Run(repo, "checkout", "-b", "feature-sync");
            GitCli.Run(repo, "checkout", "main");

            var report = Land(repo, LandingChange.WriteFile("landed.txt", "landed\n"));

            report.BranchCreated.ShouldBeFalse();
            CurrentBranch(repo).ShouldBe("feature-sync");
            File.ReadAllText(Path.Combine(repo, "landed.txt")).ShouldBe("landed\n");
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Land_DirtyBaseTree_Refuses()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            File.WriteAllText(Path.Combine(repo, "local.txt"), "human work");

            LandFails(repo, LandingChange.WriteFile("new.txt", "x")).ShouldContain("uncommitted changes");
            CurrentBranch(repo).ShouldBe("main");
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Land_InFlightMarker_RefusesAndKeepsTree()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            InFlightMarker.Set(MarkerPath(repo));

            LandFails(repo, LandingChange.WriteFile("new.txt", "x")).ShouldContain("already in flight");
            GitCli.StatusPorcelain(repo).ShouldBe("");
            InFlightMarker.Exists(MarkerPath(repo)).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Land_OpenInboxOnSyncBranch_Refuses()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            GitCli.Run(repo, "checkout", "-b", "feature-sync");

            LandFails(repo, LandingChange.WriteFile("new.txt", "x")).ShouldContain("inbox");
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Land_OffBaseBranch_Refuses()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            GitCli.Run(repo, "checkout", "-b", "experiment");

            LandFails(repo, LandingChange.WriteFile("new.txt", "x")).ShouldContain("base branch 'main'");
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Land_Rename_WritesTargetAndRemovesSource()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "old.txt", "moved content\n", "seed");

            var report = Land(repo, LandingChange.RenameFile("old.txt", "docs/renamed.txt", "moved content\n"));

            report.WrittenFiles.ShouldBe(new[] { "docs/renamed.txt" });
            report.DeletedFiles.ShouldBe(new[] { "old.txt" });
            File.Exists(Path.Combine(repo, "old.txt")).ShouldBeFalse();
            File.ReadAllText(Path.Combine(repo, "docs", "renamed.txt")).ShouldBe("moved content\n");
            GitCli.StatusPorcelain(repo).ShouldContain("?? docs/");
            GitCli.StatusPorcelain(repo).ShouldContain(" D old.txt");
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Land_SelfRename_FailsBeforeWritingAnything()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");

            LandFails(repo,
                    LandingChange.WriteFile("healthy.txt", "should never land\n"),
                    LandingChange.RenameFile("seed.txt", "seed.txt", "rewritten in place\n"))
                .ShouldContain("same path");

            GitCli.StatusPorcelain(repo).ShouldBe("");
            File.ReadAllText(Path.Combine(repo, "seed.txt")).ShouldBe("seed\n");
            File.Exists(Path.Combine(repo, "healthy.txt")).ShouldBeFalse();
            InFlightMarker.Exists(MarkerPath(repo)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Land_PathEscape_FailsAndClearsMarker()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");

            LandFails(repo, LandingChange.WriteFile("../escape.txt", "x")).ShouldContain("parent segments");
            InFlightMarker.Exists(MarkerPath(repo)).ShouldBeFalse();
            File.Exists(Path.GetFullPath(Path.Combine(repo, "../escape.txt"))).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }
}
