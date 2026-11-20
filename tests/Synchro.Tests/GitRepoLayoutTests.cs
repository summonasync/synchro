using Shouldly;
using Synchro.Core.Git;
using Xunit;

namespace Synchro.Tests;

public class GitRepoLayoutTests
{
    [Fact]
    public void IsWorkTree_GitRepo_True_PlainFolder_False()
    {
        var repo = TestGitRepo.Create();
        var plain = TempDir.Create();

        GitRepoLayout.IsWorkTree(repo).ShouldBeTrue();
        GitRepoLayout.IsWorkTree(plain).ShouldBeFalse();
    }

    [Fact]
    public void IsLinkedGitDir_WorktreeCheckout_True()
    {
        var area = new WorktreeArea();
        try
        {
            GitRepoLayout.IsLinkedGitDir(area.Linked).ShouldBeTrue();
            GitRepoLayout.IsLinkedGitDir(area.Repo).ShouldBeFalse();
        }
        finally
        {
            area.Dispose();
        }
    }

    [Fact]
    public void IsLinkedGitDir_GitdirFile_True_EvenWhenTargetIsGone()
    {
        var dir = TempDir.Create();
        try
        {
            File.WriteAllText(Path.Combine(dir, ".git"), "gitdir: /gone/repo/.git/worktrees/lost\n");

            GitRepoLayout.IsLinkedGitDir(dir).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CurrentBranch_HeadCheckout_ReturnsBranchName()
    {
        var repo = TestGitRepo.Create();
        TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");

        GitRepoLayout.CurrentBranch(repo).ShouldBe("main");
    }

    [Fact]
    public void CurrentBranch_DetachedHead_ReturnsNull()
    {
        var repo = TestGitRepo.Create();
        TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
        GitCli.Run(repo, "checkout", "--detach", "HEAD");

        GitRepoLayout.CurrentBranch(repo).ShouldBeNull();
    }

    [Fact]
    public void CurrentBranch_PlainFolder_ReturnsNull()
    {
        var plain = TempDir.Create();

        GitRepoLayout.CurrentBranch(plain).ShouldBeNull();
    }

    private sealed class WorktreeArea : IDisposable
    {
        public string Repo { get; } = TestGitRepo.Create();
        public string Linked { get; }

        public WorktreeArea()
        {
            TestGitRepo.Commit(Repo, "seed.txt", "seed\n", "seed");
            Linked = Path.Combine(TempDir.Create(), "linked");
            GitCli.Run(Repo, "worktree", "add", "-b", "wt-branch", Linked);
        }

        public void Dispose()
        {
            GitCli.Run(Repo, "worktree", "remove", "--force", Linked);
        }
    }
}
