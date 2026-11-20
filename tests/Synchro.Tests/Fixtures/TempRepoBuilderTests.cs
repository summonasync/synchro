using Shouldly;
using Xunit;

namespace Synchro.Tests.Fixtures;

public class TempRepoBuilderTests
{
    private static readonly DateTimeOffset FirstDate = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondDate = new(2026, 5, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Commit_WithPinnedDates_ProducesIdenticalShasAcrossRepos()
    {
        var first = TempRepoBuilder.Create();
        var second = TempRepoBuilder.Create();
        try
        {
            var firstRootSha = first.Commit("a.txt", "alpha\n", "add a", FirstDate);
            var firstTipSha = first.CommitFiles(
                [new RepoFile("a.txt", "alpha v2\n"), new RepoFile("nested/b.txt", "beta\n")],
                "rework a and add b",
                SecondDate);
            var secondRootSha = second.Commit("a.txt", "alpha\n", "add a", FirstDate);
            var secondTipSha = second.CommitFiles(
                [new RepoFile("a.txt", "alpha v2\n"), new RepoFile("nested/b.txt", "beta\n")],
                "rework a and add b",
                SecondDate);

            firstRootSha.ShouldBe(secondRootSha);
            firstTipSha.ShouldBe(secondTipSha);
            first.HeadSha.ShouldBe(firstTipSha);

            FixtureGit.Output(first.Root, "log", "-1", "--format=%an|%ae|%aI|%cI")
                .ShouldBe($"{TempRepoBuilder.UserName}|{TempRepoBuilder.UserEmail}|2026-05-02T09:00:00Z|2026-05-02T09:00:00Z");
        }
        finally
        {
            first.Delete();
            second.Delete();
        }
    }

    [Fact]
    public void Branch_Tag_Checkout_ManageRefsAtPinnedPoints()
    {
        var repo = TempRepoBuilder.Create();
        try
        {
            var rootSha = repo.Commit("a.txt", "alpha\n", "add a", FirstDate);
            repo.Branch("side", rootSha);
            repo.Tag("v-seed", rootSha);

            repo.Checkout("side");
            var sideSha = repo.Commit("side.txt", "side work\n", "side commit", SecondDate);

            repo.HeadSha.ShouldBe(sideSha);
            FixtureGit.Output(repo.Root, "branch", "--show-current").ShouldBe("side");
            FixtureGit.Output(repo.Root, "rev-parse", "v-seed").ShouldBe(rootSha);
            FixtureGit.Output(repo.Root, "rev-parse", "main").ShouldBe(rootSha);
            FixtureGit.Output(repo.Root, "rev-list", "--count", "main").ShouldBe("1");
        }
        finally
        {
            repo.Delete();
        }
    }
}
