using Shouldly;
using Synchro.Core;
using Synchro.Core.Landing;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class RollbackServiceTests
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

    private static RollbackReport Rollback(string repo) =>
        RollbackService.Rollback(repo, repo, Slave(repo))
            .ShouldBeOfType<Result<RollbackReport>.Ok>().Value;

    private static string RollbackFails(string repo) =>
        RollbackService.Rollback(repo, repo, Slave(repo))
            .ShouldBeOfType<Result<RollbackReport>.Fail>().Error.Message;

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
    public void Rollback_RestoresTreeIncludingDeletions_LeavesZeroCommits()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "keep.txt", "keep\n", "seed");
            TestGitRepo.Commit(repo, "gone.txt", "gone\n", "add gone");

            Land(repo,
                LandingChange.WriteFile("keep.txt", "keep\nrewritten by sync"),
                LandingChange.WriteFile("added/nested/new.txt", "sync addition"),
                new LandingChange.Delete("gone.txt"));

            var report = Rollback(repo);

            report.RolledBack.ShouldBeTrue();
            report.Branch.ShouldBe("main");
            report.RestoredPaths.ShouldBe(new[] { "gone.txt", "keep.txt" });
            report.RemovedPaths.ShouldBe(new[] { "added/" });

            CurrentBranch(repo).ShouldBe("main");
            File.ReadAllText(Path.Combine(repo, "keep.txt")).ShouldBe("keep\n");
            File.ReadAllText(Path.Combine(repo, "gone.txt")).ShouldBe("gone\n");
            File.Exists(Path.Combine(repo, "added", "nested", "new.txt")).ShouldBeFalse();
            Directory.Exists(Path.Combine(repo, "added")).ShouldBeFalse();

            CommitCount(repo).ShouldBe("2");
            GitCli.StatusPorcelain(repo).ShouldBe("");
            InFlightMarker.Exists(MarkerPath(repo)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Rollback_AfterHumanCommit_RefusesAndPointsToRevert()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            Land(repo, LandingChange.WriteFile("landed.txt", "inbox\n"));
            GitCli.Run(repo, "add", "-A");
            GitCli.Run(repo, "commit", "-m", "human kept the inbox");

            RollbackFails(repo).ShouldContain("git revert");

            CurrentBranch(repo).ShouldBe("feature-sync");
            CommitCount(repo).ShouldBe("2");
            File.Exists(Path.Combine(repo, "landed.txt")).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Rollback_CleanBaseTree_ReportsNothingToRollBack()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            InFlightMarker.Set(MarkerPath(repo));

            var report = Rollback(repo);

            report.RolledBack.ShouldBeFalse();
            report.Branch.ShouldBe("main");
            CurrentBranch(repo).ShouldBe("main");
            InFlightMarker.Exists(MarkerPath(repo)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Rollback_CleanSyncBranchWithoutCommits_ChecksOutBaseAndClearsMarker()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            GitCli.Run(repo, "checkout", "-b", "feature-sync");
            InFlightMarker.Set(MarkerPath(repo));

            var report = Rollback(repo);

            report.RolledBack.ShouldBeFalse();
            report.Branch.ShouldBe("main");
            CurrentBranch(repo).ShouldBe("main");
            GitCli.StatusPorcelain(repo).ShouldBe("");
            CommitCount(repo).ShouldBe("1");
            InFlightMarker.Exists(MarkerPath(repo)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Rollback_UndeletableUntrackedDirectory_ProducesPartialReport()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            Land(repo,
                LandingChange.WriteFile("stuck/inner.txt", "undeletable\n"),
                LandingChange.WriteFile("free.txt", "removable\n"));
            File.SetAttributes(Path.Combine(repo, "stuck"), FileAttributes.ReadOnly);

            var report = Rollback(repo);

            report.RolledBack.ShouldBeTrue();
            report.Branch.ShouldBe("main");
            report.RemovedPaths.ShouldBe(new[] { "free.txt" });
            report.Skipped.ShouldHaveSingleItem().Path.ShouldBe("stuck/");
            report.Skipped[0].Reason.ShouldNotBeEmpty();

            CurrentBranch(repo).ShouldBe("main");
            File.Exists(Path.Combine(repo, "stuck", "inner.txt")).ShouldBeTrue();
            GitCli.StatusPorcelain(repo).ShouldContain("?? stuck/");
        }
        finally
        {
            var stuck = Path.Combine(repo, "stuck");
            if (Directory.Exists(stuck))
            {
                File.SetAttributes(stuck, FileAttributes.Normal);
            }

            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Rollback_DirtyBaseTree_Refuses()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            File.WriteAllText(Path.Combine(repo, "local.txt"), "human work");

            RollbackFails(repo).ShouldContain("non-sync changes");
            CurrentBranch(repo).ShouldBe("main");
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Rollback_ForeignBranch_Refuses()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            GitCli.Run(repo, "checkout", "-b", "experiment");

            RollbackFails(repo).ShouldContain("sync branch 'feature-sync'");
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Rollback_StaleInFlightMarker_IsCleared()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            Land(repo, LandingChange.WriteFile("landed.txt", "inbox\n"));
            InFlightMarker.Set(MarkerPath(repo));

            Rollback(repo).RolledBack.ShouldBeTrue();
            InFlightMarker.Exists(MarkerPath(repo)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }
}
