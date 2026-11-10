using System.Security.Cryptography;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Apply;
using Synchro.Core.Package;
using Synchro.Core.Pack;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class ApplyPlainTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    private static StoreState PlainState(StoreState? from = null) => (from ?? new StoreState
    {
        SchemaVersion = 1,
        Channel = new ChannelConfig { Name = "docs", Peer = "shiva", Mode = SyncMode.Plain },
        Ledger = [],
        AppliedFileHashes = [],
        AppliedContents = []
    });

    private static string PackPlain(string dir, StoreState state, string outPath)
    {
        var draft = PackService.Pack(dir, state, new PackOptions())
            .ShouldBeOfType<Result<PackDraft>.Ok>().Value;
        PackageSealer.Seal(outPath, Key, draft.Manifest);
        return outPath;
    }

    private static ApplyReport Apply(string dir, StoreState state, string packagePath) =>
        ApplyService.Apply(dir, state, Key, packagePath)
            .ShouldBeOfType<Result<ApplyReport>.Ok>().Value;

    private static void Write(string dir, string path, string content)
    {
        var fullPath = Path.Combine(dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Fact]
    public void Apply_PlainBaseline_ChainAdvancesToSeq1()
    {
        var dirA = TempDir.Create();
        var dirB = TempDir.Create();
        try
        {
            Write(dirA, "a.txt", "alpha\n");
            var pkg = PackPlain(dirA, PlainState(), Path.Combine(dirA, "synchro-1.synchr"));

            var report = Apply(dirB, PlainState(), pkg);

            File.ReadAllText(Path.Combine(dirB, "a.txt")).ShouldBe("alpha\n");
            report.UpdatedState!.AppliedChainSeq.ShouldBe(1);
            report.UpdatedState.Ledger.ShouldHaveSingleItem().Direction.ShouldBe(LedgerDirection.Applied);
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Apply_PlainZeroDrift_DeltaAppliesSilently()
    {
        var dirA = TempDir.Create();
        var dirB = TempDir.Create();
        try
        {
            Write(dirA, "a.txt", "alpha\n");
            Write(dirA, "b.txt", "beta\n");
            var pkg1 = PackPlain(dirA, PlainState(), Path.Combine(dirA, "s1.synchr"));
            var stateB = Apply(dirB, PlainState(), pkg1).UpdatedState!;
            var stateA1 = PackService.Pack(dirA, PlainState(), new PackOptions())
                .ShouldBeOfType<Result<PackDraft>.Ok>().Value.UpdatedState!;

            File.WriteAllText(Path.Combine(dirA, "a.txt"), "alpha2\n");
            Write(dirA, "c.txt", "gamma\n");
            File.Delete(Path.Combine(dirA, "b.txt"));
            var pkg2 = PackPlain(dirA, stateA1, Path.Combine(dirA, "s2.synchr"));

            var report = Apply(dirB, stateB, pkg2);

            File.ReadAllText(Path.Combine(dirB, "a.txt")).ShouldBe("alpha2\n");
            File.ReadAllText(Path.Combine(dirB, "c.txt")).ShouldBe("gamma\n");
            File.Exists(Path.Combine(dirB, "b.txt")).ShouldBeFalse();
            report.AppliedFiles.ShouldBe(["a.txt", "c.txt"]);
            report.AppliedDeletions.ShouldBe(["b.txt"]);
            report.UpdatedState!.AppliedChainSeq.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Apply_PlainDriftOverlap_RoutesToSceneKeepsLocal()
    {
        var dirA = TempDir.Create();
        var dirB = TempDir.Create();
        try
        {
            Write(dirA, "a.txt", "alpha\n");
            Write(dirA, "b.txt", "beta\n");
            var pkg1 = PackPlain(dirA, PlainState(), Path.Combine(dirA, "s1.synchr"));
            var stateB = Apply(dirB, PlainState(), pkg1).UpdatedState!;
            var stateA1 = PackService.Pack(dirA, PlainState(), new PackOptions())
                .ShouldBeOfType<Result<PackDraft>.Ok>().Value.UpdatedState!;

            File.WriteAllText(Path.Combine(dirA, "a.txt"), "A version\n");
            var pkg2 = PackPlain(dirA, stateA1, Path.Combine(dirA, "s2.synchr"));

            File.WriteAllText(Path.Combine(dirB, "a.txt"), "B version\n");
            var report = Apply(dirB, stateB, pkg2);

            report.ScenePending.ShouldBeTrue();
            report.ConflictPaths.ShouldBe(["a.txt"]);
            File.ReadAllText(Path.Combine(dirB, "a.txt")).ShouldBe("B version\n");
            File.ReadAllText(Path.Combine(report.UpdatedState!.PendingScene!.SceneDir, "theirs", "a.txt"))
                .ShouldBe("A version\n");
            File.ReadAllText(Path.Combine(report.UpdatedState.PendingScene.SceneDir, "base", "a.txt"))
                .ShouldBe("alpha\n");
            report.UpdatedState.AppliedChainSeq.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Apply_PlainOutOfOrder_FailsWithChainGuidance()
    {
        var dirA = TempDir.Create();
        var dirB = TempDir.Create();
        try
        {
            Write(dirA, "a.txt", "alpha\n");
            var pkg1 = PackPlain(dirA, PlainState(), Path.Combine(dirA, "s1.synchr"));
            var stateB = Apply(dirB, PlainState(), pkg1).UpdatedState!;

            var stateA1 = PackService.Pack(dirA, PlainState(), new PackOptions())
                .ShouldBeOfType<Result<PackDraft>.Ok>().Value.UpdatedState!;
            File.WriteAllText(Path.Combine(dirA, "a.txt"), "A v2\n");
            var stateA2 = PackService.Pack(dirA, stateA1, new PackOptions())
                .ShouldBeOfType<Result<PackDraft>.Ok>().Value.UpdatedState!;
            File.WriteAllText(Path.Combine(dirA, "a.txt"), "A v3\n");
            var pkg3 = PackPlain(dirA, stateA2, Path.Combine(dirA, "s3.synchr"));

            var message = ApplyService.Apply(dirB, stateB, Key, pkg3)
                .ShouldBeOfType<Result<ApplyReport>.Fail>().Error.Message;

            message.ShouldContain("chain #2");
            message.ShouldContain("#1");
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Apply_MidApplyFailure_ReApplyHealsWithoutPhantomConflicts()
    {
        var dirA = TempDir.Create();
        var dirB = TempDir.Create();
        try
        {
            Write(dirA, "a.txt", "alpha\n");
            var pkg1 = PackPlain(dirA, PlainState(), Path.Combine(dirA, "s1.synchr"));
            var stateB = Apply(dirB, PlainState(), pkg1).UpdatedState!;
            var stateA1 = PackService.Pack(dirA, PlainState(), new PackOptions())
                .ShouldBeOfType<Result<PackDraft>.Ok>().Value.UpdatedState!;

            File.WriteAllText(Path.Combine(dirA, "a.txt"), "alpha2\n");
            Write(dirA, "sub/c.txt", "cee\n");
            Write(dirA, "d.txt", "dee\n");
            var pkg2 = PackPlain(dirA, stateA1, Path.Combine(dirA, "s2.synchr"));

            File.WriteAllText(Path.Combine(dirB, "sub"), "this file blocks the sub directory");

            var blocked = ApplyService.Apply(dirB, stateB, Key, pkg2);
            blocked.ShouldBeOfType<Result<ApplyReport>.Fail>();

            File.Delete(Path.Combine(dirB, "sub"));

            var healed = Apply(dirB, stateB, pkg2);

            healed.ScenePending.ShouldBeFalse();
            healed.ConflictPaths.ShouldBeEmpty();
            healed.AppliedFiles.ShouldBe(["a.txt", "d.txt", "sub/c.txt"]);
            File.ReadAllText(Path.Combine(dirB, "a.txt")).ShouldBe("alpha2\n");
            File.ReadAllText(Path.Combine(dirB, "sub", "c.txt")).ShouldBe("cee\n");
            healed.UpdatedState!.AppliedChainSeq.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Apply_PlainSamePackageTwice_FriendlyNoOp()
    {
        var dirA = TempDir.Create();
        var dirB = TempDir.Create();
        try
        {
            Write(dirA, "a.txt", "alpha\n");
            var pkg = PackPlain(dirA, PlainState(), Path.Combine(dirA, "s1.synchr"));
            var stateB = Apply(dirB, PlainState(), pkg).UpdatedState!;

            var second = ApplyService.Apply(dirB, stateB, Key, pkg);

            var ok = second.ShouldBeOfType<Result<ApplyReport>.Ok>();
            ok.Value.AlreadyApplied.ShouldBeTrue();
            ok.Value.UpdatedState.ShouldBeNull();
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }
}
