using Shouldly;
using Synchro.Core;
using Synchro.Core.Slave;
using Xunit;

namespace Synchro.Tests;

public class SlavePathGuardTests
{
    [Fact]
    public void Resolve_GitRepoOutsideMaster_ReturnsCanonicalRoot()
    {
        using var area = new Area();
        var slave = TestGitRepo.Create();

        var resolved = SlavePathGuard.Resolve(area.Master, slave)
            .ShouldBeOfType<Result<string>.Ok>().Value;

        resolved.ShouldBe(slave);
    }

    [Fact]
    public void Resolve_MasterRepoItself_IsRefusedWithoutWrites()
    {
        using var area = new Area();
        var before = GitCli.StatusPorcelain(area.Master);

        var error = SlavePathGuard.Resolve(area.Master, area.Master)
            .ShouldBeOfType<Result<string>.Fail>().Error;

        error.Message.ShouldContain("master repo itself");
        GitCli.StatusPorcelain(area.Master).ShouldBe(before);
    }

    [Fact]
    public void Resolve_MasterPathWithDotSegments_IsStillRefused()
    {
        using var area = new Area();

        var error = SlavePathGuard.Resolve(area.Master, Path.Combine(area.Master, ".", "docs", ".."))
            .ShouldBeOfType<Result<string>.Fail>().Error;

        error.Message.ShouldContain("master repo itself");
    }

    [Fact]
    public void Resolve_NestedInsideMaster_IsRefusedAndFolderStaysPlain()
    {
        using var area = new Area();
        var nested = Directory.CreateDirectory(Path.Combine(area.Master, "inner")).FullName;

        var error = SlavePathGuard.Resolve(area.Master, nested)
            .ShouldBeOfType<Result<string>.Fail>().Error;

        error.Message.ShouldContain("nested inside the master working tree");
        Directory.Exists(Path.Combine(nested, ".git")).ShouldBeFalse();
    }

    [Fact]
    public void Resolve_RelativePathInsideMaster_IsRefused()
    {
        using var area = new Area();
        Directory.CreateDirectory(Path.Combine(area.Master, "inner"));

        var error = SlavePathGuard.Resolve(area.Master, "inner")
            .ShouldBeOfType<Result<string>.Fail>().Error;

        error.Message.ShouldContain("nested inside the master working tree");
    }

    [Fact]
    public void Resolve_WorktreeLinkedRepo_IsRefusedWithoutWrites()
    {
        using var area = new Area();
        var repo = TestGitRepo.Create();
        TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
        var linked = Path.Combine(area.Worktrees, "linked");
        GitCli.Run(repo, "worktree", "add", "-b", "wt-branch", linked);
        var commitBefore = HeadCount(linked);

        var error = SlavePathGuard.Resolve(area.Master, linked)
            .ShouldBeOfType<Result<string>.Fail>().Error;

        error.Message.ShouldContain("worktree linked to another repo");
        HeadCount(linked).ShouldBe(commitBefore);
    }

    [Fact]
    public void Resolve_NonExistentPath_IsRefused()
    {
        using var area = new Area();
        var missing = Path.Combine(area.Master, "nowhere");

        var error = SlavePathGuard.Resolve(area.Master, missing)
            .ShouldBeOfType<Result<string>.Fail>().Error;

        error.Code.ShouldBe("not_found");
        error.Message.ShouldContain("does not exist");
    }

    private static int HeadCount(string repo) =>
        int.Parse(GitCli.Run(repo, "rev-list", "--count", "HEAD").Output.Trim());

    private sealed class Area : IDisposable
    {
        public string Master { get; }
        public string Worktrees { get; }

        public Area()
        {
            Master = TempDir.Create();
            Worktrees = TempDir.Create();
        }

        public void Dispose()
        {
            Directory.Delete(Master, true);
            Directory.Delete(Worktrees, true);
        }
    }
}
