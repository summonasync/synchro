using Shouldly;
using Synchro.Core;
using Synchro.Core.Hashing;
using Synchro.Core.Pack;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class PackServiceTests
{
    private static StoreState GitStore(string? peerKnown = null) => new()
    {
        SchemaVersion = 1,
        Channel = new ChannelConfig { Name = "slurpy", Peer = "shiva", Mode = SyncMode.Git },
        Ledger = [],
        AppliedFileHashes = [],
        AppliedContents = [],
        PeerKnownLocalPosition = peerKnown
    };

    private static Result<PackDraft>.Ok Pack(string dir, StoreState state, bool full = false) =>
        PackService.Pack(dir, state, new PackOptions { Full = full }).ShouldBeOfType<Result<PackDraft>.Ok>();

    [Fact]
    public void Pack_FullBaseline_ContainsAllTrackedFiles()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "src/a.txt", "alpha\n", "add a");
            TestGitRepo.Commit(dir, "src/b.txt", "beta\n", "add b");

            var draft = Pack(dir, GitStore(), full: true);

            draft.Value.Manifest.BasePosition.ShouldBeNull();
            draft.Value.Manifest.Files.Select(f => f.Path).ShouldBe(["src/a.txt", "src/b.txt"]);
            draft.Value.Manifest.Commits.Count.ShouldBe(2);
            draft.Value.Manifest.Features.ShouldBeEmpty();
            draft.Value.Warnings.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pack_DeltaSincePeerPosition_EmitsOnlyChangedFiles()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "a.txt", "one\n", "first");
            var baseSha = TestGitRepo.Commit(dir, "b.txt", "bee\n", "add b");
            TestGitRepo.Commit(dir, "b.txt", "bee2\n", "modify b");
            TestGitRepo.Commit(dir, "c.txt", "cee\n", "add c");

            var draft = Pack(dir, GitStore(peerKnown: baseSha));

            draft.Value.Manifest.BasePosition.ShouldBe(new GitPosition(baseSha));
            draft.Value.Manifest.Files.Select(f => f.Path).ShouldBe(["b.txt", "c.txt"]);
            draft.Value.Manifest.Commits.Count.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pack_DeletedFiles_ListedInDeletions()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "a.txt", "one\n", "first");
            var baseSha = TestGitRepo.Commit(dir, "b.txt", "bee\n", "add b");
            TestGitRepo.DeleteAndCommit(dir, "a.txt", "drop a");

            var draft = Pack(dir, GitStore(peerKnown: baseSha));

            draft.Value.Manifest.Deletions.ShouldBe(["a.txt"]);
            draft.Value.Manifest.Files.Select(f => f.Path).ShouldNotContain("a.txt");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pack_FileHashes_MatchCanonicalContentHash()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "a.txt", "alpha\n", "add a");

            var draft = Pack(dir, GitStore(), full: true);

            var entry = draft.Value.Manifest.Files.ShouldHaveSingleItem();
            entry.Hash.ShouldBe(ContentHash.Compute("alpha\n"u8.ToArray()));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pack_DirtyTree_WarnsAndPacksCommittedState()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "a.txt", "committed\n", "add a");
            File.WriteAllText(Path.Combine(dir, "a.txt"), "dirty local edit\n");

            var draft = Pack(dir, GitStore(), full: true);

            draft.Value.Warnings.ShouldContain(w => w.Contains("uncommitted"));
            draft.Value.Manifest.Files.ShouldHaveSingleItem()
                .Hash.ShouldBe(ContentHash.Compute("committed\n"u8.ToArray()));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pack_NoPeerPositionWithoutFull_FailsWithGuidance()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "a.txt", "one\n", "first");

            var result = PackService.Pack(dir, GitStore(), new PackOptions());

            var fail = result.ShouldBeOfType<Result<PackDraft>.Fail>();
            fail.Error.Message.ShouldContain("--full");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pack_StagedUncommittedFile_ExcludedFromFullBaseline()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "a.txt", "committed\n", "add a");
            TestGitRepo.Stage(dir, "b.txt", "staged but not committed\n");

            var draft = Pack(dir, GitStore(), full: true);

            var paths = draft.Value.Manifest.Files.Select(f => f.Path).ToList();
            paths.ShouldBe(["a.txt"]);
            paths.ShouldNotContain("b.txt");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pack_PeerPositionOnUnrelatedBranch_FailsWithConflict()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "a.txt", "one\n", "first");
            GitCli.Run(dir, "checkout", "--orphan", "side");
            GitCli.Run(dir, "add", "-A");
            GitCli.Run(dir, "commit", "-m", "orphan commit");
            var orphanSha = GitCli.Run(dir, "rev-parse", "HEAD").Output.Trim();
            GitCli.Run(dir, "checkout", "main");

            var result = PackService.Pack(dir, GitStore(peerKnown: orphanSha), new PackOptions());

            var fail = result.ShouldBeOfType<Result<PackDraft>.Fail>();
            fail.Error.Code.ShouldBe("conflict");
            fail.Error.Message.ShouldContain("history");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pack_StalePeerPosition_FailsWithMergeBaseError()
    {
        var dir = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(dir, "a.txt", "one\n", "first");
            var foreignSha = new string('f', 40);

            var result = PackService.Pack(dir, GitStore(peerKnown: foreignSha), new PackOptions());

            var fail = result.ShouldBeOfType<Result<PackDraft>.Fail>();
            fail.Error.Code.ShouldBe("invalid");
            fail.Error.Message.ShouldContain("merge-base");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pack_EmptyRepo_FailsWithCommitHint()
    {
        var dir = TestGitRepo.Create();
        try
        {
            var result = PackService.Pack(dir, GitStore(), new PackOptions { Full = true });

            var fail = result.ShouldBeOfType<Result<PackDraft>.Fail>();
            fail.Error.Message.ShouldContain("commit");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
