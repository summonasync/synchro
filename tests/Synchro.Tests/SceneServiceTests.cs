using System.Security.Cryptography;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Apply;
using Synchro.Core.Package;
using Synchro.Core.Pack;
using Synchro.Core.Scene;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class SceneServiceTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    private static (string DirA, string DirB, StoreState ConflictedB, string PkgPath) ConflictedPair(
        string aVersion, string bVersion)
    {
        var dirA = TempDir.Create();
        var dirB = TempDir.Create();

        File.WriteAllText(Path.Combine(dirA, "a.txt"), "alpha\n");
        File.WriteAllText(Path.Combine(dirA, "b.txt"), "beta\n");
        var pkg1 = Path.Combine(dirA, "s1.synchr");
        var draft1 = PackService.Pack(dirA, PlainState(), new PackOptions())
            .ShouldBeOfType<Result<PackDraft>.Ok>().Value;
        PackageSealer.Seal(pkg1, Key, draft1.Manifest);
        var stateB = ApplyService.Apply(dirB, PlainState(), Key, pkg1)
            .ShouldBeOfType<Result<ApplyReport>.Ok>().Value.UpdatedState!;
        var stateA1 = PackService.Pack(dirA, PlainState(), new PackOptions())
            .ShouldBeOfType<Result<PackDraft>.Ok>().Value.UpdatedState!;

        File.WriteAllText(Path.Combine(dirA, "a.txt"), aVersion);
        var pkg2 = Path.Combine(dirA, "s2.synchr");
        var draft2 = PackService.Pack(dirA, stateA1, new PackOptions())
            .ShouldBeOfType<Result<PackDraft>.Ok>().Value;
        PackageSealer.Seal(pkg2, Key, draft2.Manifest);

        File.WriteAllText(Path.Combine(dirB, "a.txt"), bVersion);
        var conflicted = ApplyService.Apply(dirB, stateB, Key, pkg2)
            .ShouldBeOfType<Result<ApplyReport>.Ok>().Value;

        return (dirA, dirB, conflicted.UpdatedState!, pkg2);
    }

    private static StoreState PlainState() => new()
    {
        SchemaVersion = 1,
        Channel = new ChannelConfig { Name = "docs", Peer = "shiva", Mode = SyncMode.Plain },
        Ledger = [],
        AppliedFileHashes = [],
        AppliedContents = []
    };

    [Fact]
    public void Resolve_Replace_ThenCommit_TakesTheirsAndAdvancesChain()
    {
        var (dirA, dirB, conflicted, pkg) = ConflictedPair("A version\n", "B version\n");
        try
        {
            var resolved = SceneService.Resolve(dirB, conflicted, "a.txt", SceneService.OpReplace)
                .ShouldBeOfType<Result<StoreState>.Ok>().Value;

            File.ReadAllText(Path.Combine(dirB, "a.txt")).ShouldBe("A version\n");

            var committed = SceneService.Commit(dirB, resolved, Key)
                .ShouldBeOfType<Result<ApplyReport>.Ok>().Value;

            committed.UpdatedState!.PendingScene.ShouldBeNull();
            committed.UpdatedState.AppliedChainSeq.ShouldBe(2);
            Directory.GetDirectories(Path.Combine(dirB, ".synchro", "scene")).ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Resolve_Drop_ThenCommit_KeepsLocalVersion()
    {
        var (dirA, dirB, conflicted, pkg) = ConflictedPair("A version\n", "B version\n");
        try
        {
            var resolved = SceneService.Resolve(dirB, conflicted, "a.txt", SceneService.OpDrop)
                .ShouldBeOfType<Result<StoreState>.Ok>().Value;

            var committed = SceneService.Commit(dirB, resolved, Key)
                .ShouldBeOfType<Result<ApplyReport>.Ok>().Value;

            File.ReadAllText(Path.Combine(dirB, "a.txt")).ShouldBe("B version\n");
            committed.UpdatedState!.AppliedChainSeq.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Resolve_Merge_WritesMarkers_ThenResolvedFinalizes()
    {
        var (dirA, dirB, conflicted, pkg) = ConflictedPair("A line\nshared tail\n", "B line\nshared tail\n");
        try
        {
            var merged = SceneService.Resolve(dirB, conflicted, "a.txt", SceneService.OpMerge)
                .ShouldBeOfType<Result<StoreState>.Ok>().Value;

            var working = File.ReadAllText(Path.Combine(dirB, "a.txt"));
            working.ShouldContain("<<<<<<<");
            working.ShouldContain("A line");
            working.ShouldContain("B line");

            SceneService.Resolve(dirB, merged, "a.txt", SceneService.OpResolved)
                .ShouldBeOfType<Result<StoreState>.Fail>()
                .Error.Message.ShouldContain("markers");

            File.WriteAllText(Path.Combine(dirB, "a.txt"), "Merged line\nshared tail\n");
            var finalized = SceneService.Resolve(dirB, merged, "a.txt", SceneService.OpResolved)
                .ShouldBeOfType<Result<StoreState>.Ok>().Value;

            var committed = SceneService.Commit(dirB, finalized, Key)
                .ShouldBeOfType<Result<ApplyReport>.Ok>().Value;

            File.ReadAllText(Path.Combine(dirB, "a.txt")).ShouldBe("Merged line\nshared tail\n");
            committed.UpdatedState!.AppliedChainSeq.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Resolve_DeletionConflict_ReplaceDeletesMergeFails()
    {
        var dirA = TempDir.Create();
        var dirB = TempDir.Create();
        try
        {
            File.WriteAllText(Path.Combine(dirA, "a.txt"), "alpha\n");
            File.WriteAllText(Path.Combine(dirA, "b.txt"), "beta\n");
            var pkg1 = Path.Combine(dirA, "s1.synchr");
            var draft1 = PackService.Pack(dirA, PlainState(), new PackOptions())
                .ShouldBeOfType<Result<PackDraft>.Ok>().Value;
            PackageSealer.Seal(pkg1, Key, draft1.Manifest);
            var stateB = ApplyService.Apply(dirB, PlainState(), Key, pkg1)
                .ShouldBeOfType<Result<ApplyReport>.Ok>().Value.UpdatedState!;
            var stateA1 = PackService.Pack(dirA, PlainState(), new PackOptions())
                .ShouldBeOfType<Result<PackDraft>.Ok>().Value.UpdatedState!;

            File.Delete(Path.Combine(dirA, "a.txt"));
            var pkg2 = Path.Combine(dirA, "s2.synchr");
            var draft2 = PackService.Pack(dirA, stateA1, new PackOptions())
                .ShouldBeOfType<Result<PackDraft>.Ok>().Value;
            PackageSealer.Seal(pkg2, Key, draft2.Manifest);

            File.WriteAllText(Path.Combine(dirB, "a.txt"), "B keeps a\n");
            var conflicted = ApplyService.Apply(dirB, stateB, Key, pkg2)
                .ShouldBeOfType<Result<ApplyReport>.Ok>().Value;

            conflicted.ScenePending.ShouldBeTrue();

            var merge = SceneService.Resolve(dirB, conflicted.UpdatedState!, "a.txt", SceneService.OpMerge);
            merge.ShouldBeOfType<Result<StoreState>.Fail>();

            var replaced = SceneService.Resolve(dirB, conflicted.UpdatedState!, "a.txt", SceneService.OpReplace)
                .ShouldBeOfType<Result<StoreState>.Ok>().Value;

            File.Exists(Path.Combine(dirB, "a.txt")).ShouldBeFalse();

            var committed = SceneService.Commit(dirB, replaced, Key)
                .ShouldBeOfType<Result<ApplyReport>.Ok>().Value;
            committed.UpdatedState!.AppliedChainSeq.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Commit_UnresolvedConflicts_FailsWithList()
    {
        var (_, dirB, conflicted, _) = ConflictedPair("A version\n", "B version\n");
        try
        {
            var message = SceneService.Commit(dirB, conflicted, Key)
                .ShouldBeOfType<Result<ApplyReport>.Fail>().Error.Message;

            message.ShouldContain("unresolved");
            message.ShouldContain("a.txt");
        }
        finally
        {
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Resolve_Twice_FailsAsAlreadyResolved()
    {
        var (_, dirB, conflicted, _) = ConflictedPair("A version\n", "B version\n");
        try
        {
            var first = SceneService.Resolve(dirB, conflicted, "a.txt", SceneService.OpDrop)
                .ShouldBeOfType<Result<StoreState>.Ok>().Value;

            var second = SceneService.Resolve(dirB, first, "a.txt", SceneService.OpReplace);

            var fail = second.ShouldBeOfType<Result<StoreState>.Fail>();
            fail.Error.Message.ShouldContain("already resolved");
        }
        finally
        {
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Resolve_ForeignPath_FailsAsNotPartOfScene()
    {
        var (_, dirB, conflicted, _) = ConflictedPair("A version\n", "B version\n");
        try
        {
            var result = SceneService.Resolve(dirB, conflicted, "not-in-scene.txt", SceneService.OpDrop);

            var fail = result.ShouldBeOfType<Result<StoreState>.Fail>();
            fail.Error.Message.ShouldContain("not part of this scene");
        }
        finally
        {
            Directory.Delete(dirB, true);
        }
    }
}
