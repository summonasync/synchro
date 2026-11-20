using Shouldly;
using Synchro.Core.Landing;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class SyncPreconditionsTests
{
    private static string Marker(string repo) => InFlightMarker.PathFor(repo, "laptop");

    private static string Check(string repo) =>
        SyncPreconditions.Check(repo, Marker(repo), "feature-sync", "main")?.Message
            ?? throw new InvalidOperationException("expected a refusal");

    private static string Repo()
    {
        var repo = TestGitRepo.Create();
        GitHiding.EnsureExcluded(repo);
        return repo;
    }

    [Fact]
    public void Check_CleanBaseTree_Passes()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");

            SyncPreconditions.Check(repo, Marker(repo), "feature-sync", "main").ShouldBeNull();
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Check_DirtyTree_Refuses()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            File.WriteAllText(Path.Combine(repo, "dirty.txt"), "local work");

            Check(repo).ShouldContain("uncommitted changes");
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Check_OffBaseBranch_Refuses()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            GitCli.Run(repo, "checkout", "-b", "experiment");

            Check(repo).ShouldContain("base branch 'main'");
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Check_InFlightMarker_Refuses()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            InFlightMarker.Set(Marker(repo));

            Check(repo).ShouldContain("already in flight");
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Check_OpenInboxOnSyncBranch_Refuses()
    {
        var repo = Repo();
        try
        {
            TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
            GitCli.Run(repo, "checkout", "-b", "feature-sync");

            Check(repo).ShouldContain("inbox");
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void Check_RefusalRules_HaveDistinctMessages()
    {
        var messages = new Dictionary<string, Func<string>>
        {
            ["marker"] = () =>
            {
                var repo = Repo();
                try
                {
                    TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
                    InFlightMarker.Set(Marker(repo));
                    return Check(repo);
                }
                finally
                {
                    Directory.Delete(repo, true);
                }
            },
            ["inbox"] = () =>
            {
                var repo = Repo();
                try
                {
                    TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
                    GitCli.Run(repo, "checkout", "-b", "feature-sync");
                    return Check(repo);
                }
                finally
                {
                    Directory.Delete(repo, true);
                }
            },
            ["branch"] = () =>
            {
                var repo = Repo();
                try
                {
                    TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
                    GitCli.Run(repo, "checkout", "-b", "experiment");
                    return Check(repo);
                }
                finally
                {
                    Directory.Delete(repo, true);
                }
            },
            ["dirty"] = () =>
            {
                var repo = Repo();
                try
                {
                    TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
                    File.WriteAllText(Path.Combine(repo, "dirty.txt"), "local work");
                    return Check(repo);
                }
                finally
                {
                    Directory.Delete(repo, true);
                }
            }
        };

        var seen = messages.Values.Select(f => f()).ToList();
        seen.Count.ShouldBe(4);
        seen.Distinct().Count().ShouldBe(4);
    }
}
