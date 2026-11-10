using Shouldly;
using Synchro.Core;
using Synchro.Core.Git;
using Xunit;

namespace Synchro.Tests;

public class GitChangeServiceTests
{
    [Fact]
    public void GetHeadSha_CommittedRepo_ReturnsFullSha()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "a.txt", "one\n", "first");

            var head = GitChangeService.GetHeadSha(dir);

            var ok = head.ShouldBeOfType<Result<string>.Ok>();
            ok.Value.Length.ShouldBe(40);
            ok.Value.ToCharArray().ShouldAllBe(c => Uri.IsHexDigit(c));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetHeadSha_EmptyRepo_FailsWithCommitHint()
    {
        var dir = TestGitRepo.Create();
        try
        {
            var head = GitChangeService.GetHeadSha(dir);

            var fail = head.ShouldBeOfType<Result<string>.Fail>();
            fail.Error.Message.ShouldContain("commit");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetChanges_CommitsSinceBase_SplitAddedModifiedDeleted()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "a.txt", "one\n", "first");
            TestGitRepo.Commit(dir, "b.txt", "bee\n", "second");
            var baseSha = GitChangeService.GetHeadSha(dir).ShouldBeOfType<Result<string>.Ok>().Value;
            TestGitRepo.Commit(dir, "b.txt", "bee2\n", "modify b");
            TestGitRepo.Commit(dir, "c.txt", "cee\n", "add c");
            TestGitRepo.DeleteAndCommit(dir, "a.txt", "drop a");
            TestGitRepo.Commit(dir, "d.txt", "dee\n", "add d");

            var changes = GitChangeService.GetChanges(dir, baseSha);

            var ok = changes.ShouldBeOfType<Result<GitChanges>.Ok>();
            ok.Value.Added.ShouldBe(["c.txt", "d.txt"], true);
            ok.Value.Modified.ShouldBe(["b.txt"]);
            ok.Value.Deleted.ShouldBe(["a.txt"]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetChanges_InvalidBase_FailsCleanly()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "a.txt", "one\n", "first");

            var changes = GitChangeService.GetChanges(dir, new string('f', 40));

            changes.ShouldBeOfType<Result<GitChanges>.Fail>();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetCommitLog_RangeFromBase_MatchesCommitsAndFiles()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "a.txt", "one\n", "first");
            var baseSha = GitChangeService.GetHeadSha(dir).ShouldBeOfType<Result<string>.Ok>().Value;
            var secondSha = TestGitRepo.Commit(dir, "b.txt", "bee\n", "add b");
            var thirdSha = TestGitRepo.Commit(dir, "c.txt", "cee\n", "add c\n\nbody ignored");

            var log = GitChangeService.GetCommitLog(dir, baseSha);

            var ok = log.ShouldBeOfType<Result<IReadOnlyList<CommitInfo>>.Ok>();
            ok.Value.Count.ShouldBe(2);
            ok.Value[0].Sha.ShouldBe(thirdSha);
            ok.Value[0].Message.ShouldBe("add c");
            ok.Value[0].Files.ShouldBe(["c.txt"]);
            ok.Value[1].Sha.ShouldBe(secondSha);
            ok.Value[1].Files.ShouldBe(["b.txt"]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetCommitLog_WithoutBase_ReturnsFullHistory()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "a.txt", "one\n", "first");
            TestGitRepo.Commit(dir, "b.txt", "bee\n", "second");

            var log = GitChangeService.GetCommitLog(dir, null);

            var ok = log.ShouldBeOfType<Result<IReadOnlyList<CommitInfo>>.Ok>();
            ok.Value.Count.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetHeadTreeFiles_ListsCommittedTreeOnly()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "src/a.txt", "one\n", "first");
            TestGitRepo.Commit(dir, "src/b.txt", "bee\n", "second");
            TestGitRepo.Stage(dir, "src/c.txt", "staged only\n");

            GitChangeService.GetHeadTreeFiles(dir).ShouldBe(["src/a.txt", "src/b.txt"], true);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
