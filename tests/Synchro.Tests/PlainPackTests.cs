using Shouldly;
using Synchro.Core;
using Synchro.Core.Hashing;
using Synchro.Core.Pack;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class PlainPackTests
{
    private static StoreState FreshPlainStore() => new()
    {
        SchemaVersion = 1,
        Channel = new ChannelConfig { Name = "docs", Peer = "shiva", Mode = SyncMode.Plain },
        Ledger = [],
        AppliedFileHashes = [],
        AppliedContents = []
    };

    private static Result<PackDraft>.Ok Pack(string dir, StoreState state) =>
        PackService.Pack(dir, state, new PackOptions()).ShouldBeOfType<Result<PackDraft>.Ok>();

    private static void Write(string dir, string path, string content)
    {
        var fullPath = Path.Combine(dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Fact]
    public void Pack_PlainFirstPackage_BaselinesAllFiles()
    {
        var dir = TempDir.Create();
        try
        {
            Write(dir, "a.txt", "alpha\n");
            Write(dir, "sub/b.txt", "beta\n");

            var draft = Pack(dir, FreshPlainStore());

            var manifest = draft.Value.Manifest;
            manifest.Mode.ShouldBe(SyncMode.Plain);
            manifest.BasePosition.ShouldBeNull();
            manifest.NewPosition.ShouldBeOfType<ChainPosition>();
            var chain = manifest.NewPosition.ShouldBeOfType<ChainPosition>();
            chain.Seq.ShouldBe(1);
            manifest.Files.Select(f => f.Path).ShouldBe(["a.txt", "sub/b.txt"]);
            draft.Value.UpdatedState.ShouldNotBeNull();
            draft.Value.UpdatedState.SentChainSeq.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pack_PlainSecondPackage_EmitsHashVerifiedDelta()
    {
        var dir = TempDir.Create();
        try
        {
            Write(dir, "a.txt", "alpha\n");
            Write(dir, "b.txt", "beta\n");
            var first = Pack(dir, FreshPlainStore());
            var stateAfterFirst = first.Value.UpdatedState!;

            File.WriteAllText(Path.Combine(dir, "a.txt"), "alpha2\n");
            Write(dir, "c.txt", "gamma\n");
            File.Delete(Path.Combine(dir, "b.txt"));

            var second = Pack(dir, stateAfterFirst);

            var manifest = second.Value.Manifest;
            var chain = manifest.NewPosition.ShouldBeOfType<ChainPosition>();
            chain.Seq.ShouldBe(2);
            manifest.BasePosition.ShouldBe(first.Value.Manifest.NewPosition);
            manifest.Files.Select(f => f.Path).ShouldBe(["a.txt", "c.txt"]);
            var aEntry = manifest.Files.Single(f => f.Path == "a.txt");
            aEntry.Hash.ShouldBe(ContentHash.Compute("alpha2\n"u8.ToArray()));
            aEntry.Content.ShouldBe("alpha2\n"u8.ToArray());
            manifest.Deletions.ShouldBe(["b.txt"]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pack_PlainUnchangedTree_FailsCleanly()
    {
        var dir = TempDir.Create();
        try
        {
            Write(dir, "a.txt", "alpha\n");
            var first = Pack(dir, FreshPlainStore());

            var second = PackService.Pack(dir, first.Value.UpdatedState!, new PackOptions());

            var fail = second.ShouldBeOfType<Result<PackDraft>.Fail>();
            fail.Error.Message.ShouldContain("nothing changed");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pack_PlainRootHash_DeterministicAcrossIdenticalTrees()
    {
        var dirA = TempDir.Create();
        var dirB = TempDir.Create();
        try
        {
            Write(dirA, "x/one.txt", "one\n");
            Write(dirA, "two.txt", "two\n");
            Write(dirB, "two.txt", "two\n");
            Write(dirB, "x/one.txt", "one\n");

            var packA = Pack(dirA, FreshPlainStore());
            var packB = Pack(dirB, FreshPlainStore());

            var chainA = packA.Value.Manifest.NewPosition.ShouldBeOfType<ChainPosition>();
            var chainB = packB.Value.Manifest.NewPosition.ShouldBeOfType<ChainPosition>();
            chainA.RootHash.ShouldBe(chainB.RootHash);
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Pack_PlainSkipsStoreAndGitDirs()
    {
        var dir = TempDir.Create();
        try
        {
            Write(dir, "a.txt", "alpha\n");
            Write(dir, ".synchro/leak.txt", "secret store junk\n");
            Write(dir, ".git/leak.txt", "not a real repo\n");

            var draft = Pack(dir, FreshPlainStore());

            draft.Value.Manifest.Files.Select(f => f.Path).ShouldBe(["a.txt"]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pack_PlainSkipsSealedPackages_NoSelfReference()
    {
        var dir = TempDir.Create();
        try
        {
            Write(dir, "a.txt", "alpha\n");
            File.WriteAllBytes(Path.Combine(dir, "synchro-1.synchr"), [0x11, 0x22]);

            var first = Pack(dir, FreshPlainStore());

            first.Value.Manifest.Files.Select(f => f.Path).ShouldBe(["a.txt"]);
            first.Value.UpdatedState!.SentFileHashes.ShouldNotContainKey("synchro-1.synchr");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
