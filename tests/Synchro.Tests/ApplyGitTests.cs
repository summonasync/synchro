using System.Security.Cryptography;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Apply;
using Synchro.Core.Package;
using Synchro.Core.Pack;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class ApplyGitTests
{
    private static readonly byte[] Key =
    [
        0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
        0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff, 0x00,
        0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
        0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff, 0x01
    ];

    private static StoreState GitState(
        string? peerKnown = null,
        string? peerHead = null,
        StoreState? from = null) => (from ?? new StoreState
    {
        SchemaVersion = 1,
        Channel = new ChannelConfig { Name = "slurpy", Peer = "shiva", Mode = SyncMode.Git },
        Ledger = [],
        AppliedFileHashes = [],
        AppliedContents = []
    }) with { PeerKnownLocalPosition = peerKnown, PeerHeadPosition = peerHead };

    private static string PackGit(string repo, StoreState state, bool full, byte[] key, string outPath)
    {
        var draft = PackService.Pack(repo, state, new PackOptions { Full = full })
            .ShouldBeOfType<Result<PackDraft>.Ok>().Value;
        PackageSealer.Seal(outPath, key, draft.Manifest);
        return outPath;
    }

    private static ApplyReport Apply(string repo, StoreState state, string packagePath)
    {
        return ApplyService.Apply(repo, state, Key, packagePath)
            .ShouldBeOfType<Result<ApplyReport>.Ok>().Value;
    }

    private static string ApplyFail(string repo, StoreState state, string packagePath)
    {
        return ApplyService.Apply(repo, state, Key, packagePath)
            .ShouldBeOfType<Result<ApplyReport>.Fail>().Error.Message;
    }

    [Fact]
    public void Apply_Baseline_CopiesFilesAndAdvancesState()
    {
        var repoA = TestGitRepo.Create();
        var repoB = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(repoA, "a.txt", "from A\n", "add a");
            var headA = TestGitRepo.Commit(repoA, "b.txt", "bee from A\n", "add b");
            TestGitRepo.Commit(repoB, "seed.txt", "seed\n", "seed");

            var pkg = PackGit(repoA, GitState(), full: true, Key, Path.Combine(repoA, "p.synchr"));
            var report = Apply(repoB, GitState(), pkg);

            File.ReadAllText(Path.Combine(repoB, "a.txt")).ShouldBe("from A\n");
            File.ReadAllText(Path.Combine(repoB, "b.txt")).ShouldBe("bee from A\n");
            File.ReadAllText(Path.Combine(repoB, "seed.txt")).ShouldBe("seed\n");
            report.UpdatedState!.PeerHeadPosition.ShouldBe(headA);
            report.ScenePending.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(repoA, true);
            Directory.Delete(repoB, true);
        }
    }

    [Fact]
    public void Apply_DeltaNonOverlap_SilentApplyAdvancesLedger()
    {
        var repoA = TestGitRepo.Create();
        var repoB = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(repoA, "a.txt", "from A\n", "add a");
            var headA = TestGitRepo.Commit(repoA, "b.txt", "bee from A\n", "add b");
            var headSeedB = TestGitRepo.Commit(repoB, "seed.txt", "seed\n", "seed");

            var baseline = PackGit(repoA, GitState(), full: true, Key, Path.Combine(repoA, "p0.synchr"));
            var bState = Apply(repoB, GitState(), baseline).UpdatedState!;

            TestGitRepo.Commit(repoA, "c.txt", "cee\n", "add c");
            var delta = PackGit(repoA, GitState(peerKnown: headA, peerHead: headSeedB), full: false, Key, Path.Combine(repoA, "p1.synchr"));

            var report = Apply(repoB, bState, delta);

            File.ReadAllText(Path.Combine(repoB, "c.txt")).ShouldBe("cee\n");
            report.AppliedFiles.ShouldBe(["c.txt"]);
            report.UpdatedState!.Ledger.Count.ShouldBe(2);
            report.UpdatedState.PeerKnownLocalPosition.ShouldBe(headSeedB);
        }
        finally
        {
            Directory.Delete(repoA, true);
            Directory.Delete(repoB, true);
        }
    }

    [Fact]
    public void Apply_Overlap_RoutesToSceneKeepsLocalVersion()
    {
        var repoA = TestGitRepo.Create();
        var repoB = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(repoA, "a.txt", "from A\n", "add a");
            var headA = TestGitRepo.Commit(repoA, "b.txt", "bee from A\n", "add b");
            var headSeedB = TestGitRepo.Commit(repoB, "seed.txt", "seed\n", "seed");

            var baseline = PackGit(repoA, GitState(), full: true, Key, Path.Combine(repoA, "p0.synchr"));
            var bState = Apply(repoB, GitState(), baseline).UpdatedState!;

            TestGitRepo.Commit(repoA, "a.txt", "A rework\n", "a rework");
            TestGitRepo.Commit(repoB, "a.txt", "B rework\n", "b rework");

            var delta = PackGit(repoA, GitState(peerKnown: headA, peerHead: headSeedB), full: false, Key, Path.Combine(repoA, "p1.synchr"));
            var report = Apply(repoB, bState, delta);

            report.ScenePending.ShouldBeTrue();
            report.ConflictPaths.ShouldBe(["a.txt"]);
            File.ReadAllText(Path.Combine(repoB, "a.txt")).ShouldBe("B rework\n");
            File.ReadAllText(Path.Combine(report.UpdatedState!.PendingScene!.SceneDir, "theirs", "a.txt"))
                .ShouldBe("A rework\n");
            File.ReadAllText(Path.Combine(report.UpdatedState.PendingScene.SceneDir, "base", "a.txt"))
                .ShouldBe("from A\n");
            report.UpdatedState.Ledger.Count.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(repoA, true);
            Directory.Delete(repoB, true);
        }
    }

    [Fact]
    public void Apply_DeletionOfLocallyChangedFile_Conflicts()
    {
        var repoA = TestGitRepo.Create();
        var repoB = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(repoA, "a.txt", "from A\n", "add a");
            var headA = TestGitRepo.Commit(repoA, "b.txt", "bee from A\n", "add b");
            var headSeedB = TestGitRepo.Commit(repoB, "seed.txt", "seed\n", "seed");

            var baseline = PackGit(repoA, GitState(), full: true, Key, Path.Combine(repoA, "p0.synchr"));
            var bState = Apply(repoB, GitState(), baseline).UpdatedState!;

            TestGitRepo.DeleteAndCommit(repoA, "b.txt", "drop b");
            TestGitRepo.Commit(repoB, "b.txt", "B keeps b\n", "b edits b");

            var delta = PackGit(repoA, GitState(peerKnown: headA, peerHead: headSeedB), full: false, Key, Path.Combine(repoA, "p1.synchr"));
            var report = Apply(repoB, bState, delta);

            report.ScenePending.ShouldBeTrue();
            report.ConflictPaths.ShouldContain("b.txt");
            File.Exists(Path.Combine(repoB, "b.txt")).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(repoA, true);
            Directory.Delete(repoB, true);
        }
    }

    [Fact]
    public void Apply_DeltaBeforeBaseline_FailsWithGuidance()
    {
        var repoB = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(repoB, "seed.txt", "seed\n", "seed");

            var manifest = new SyncManifest
            {
                SchemaVersion = 1,
                Channel = "slurpy",
                Mode = SyncMode.Git,
                BasePosition = new GitPosition(new string('f', 40)),
                AckPosition = new GitPosition(new string('e', 40)),
                NewPosition = new GitPosition(new string('a', 40)),
                Features = [],
                Commits = [],
                Files = [],
                Deletions = []
            };
            var pkg = Path.Combine(repoB, "delta.synchr");
            PackageSealer.Seal(pkg, Key, manifest);

            var message = ApplyFail(repoB, GitState(), pkg);

            message.ShouldContain("no baseline applied yet");
        }
        finally
        {
            Directory.Delete(repoB, true);
        }
    }

    [Fact]
    public void Apply_DeltaFromUnknownBase_FailsAsOutOfOrder()
    {
        var repoA = TestGitRepo.Create();
        var repoB = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(repoA, "a.txt", "from A\n", "add a");
            TestGitRepo.Commit(repoB, "seed.txt", "seed\n", "seed");
            var headSeedB = TestGitRepo.Commit(repoB, "more.txt", "more\n", "more");

            var baseline = PackGit(repoA, GitState(), full: true, Key, Path.Combine(repoA, "p0.synchr"));
            var bState = Apply(repoB, GitState(), baseline).UpdatedState!;

            var manifest = new SyncManifest
            {
                SchemaVersion = 1,
                Channel = "slurpy",
                Mode = SyncMode.Git,
                BasePosition = new GitPosition(new string('f', 40)),
                AckPosition = new GitPosition(headSeedB),
                NewPosition = new GitPosition(new string('a', 40)),
                Features = [],
                Commits = [],
                Files = [],
                Deletions = []
            };
            var pkg = Path.Combine(repoB, "lost.synchr");
            PackageSealer.Seal(pkg, Key, manifest);

            var message = ApplyFail(repoB, bState, pkg);

            message.ShouldContain("out-of-order or lost package");
        }
        finally
        {
            Directory.Delete(repoA, true);
            Directory.Delete(repoB, true);
        }
    }

    [Fact]
    public void Apply_SamePackageTwice_SecondIsFriendlyNoOp()
    {
        var repoA = TestGitRepo.Create();
        var repoB = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(repoA, "a.txt", "from A\n", "add a");
            TestGitRepo.Commit(repoB, "seed.txt", "seed\n", "seed");

            var pkg = PackGit(repoA, GitState(), full: true, Key, Path.Combine(repoA, "p.synchr"));
            var first = Apply(repoB, GitState(), pkg);

            var second = ApplyService.Apply(repoB, first.UpdatedState!, Key, pkg);

            var ok = second.ShouldBeOfType<Result<ApplyReport>.Ok>();
            ok.Value.AlreadyApplied.ShouldBeTrue();
            ok.Value.UpdatedState.ShouldBeNull();
        }
        finally
        {
            Directory.Delete(repoA, true);
            Directory.Delete(repoB, true);
        }
    }

    [Fact]
    public void Apply_WrongKey_FailsCleanly()
    {
        var repoA = TestGitRepo.Create();
        var repoB = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(repoA, "a.txt", "from A\n", "add a");
            TestGitRepo.Commit(repoB, "seed.txt", "seed\n", "seed");

            var pkg = PackGit(repoA, GitState(), full: true, Key, Path.Combine(repoA, "p.synchr"));

            var result = ApplyService.Apply(repoB, GitState(), RandomNumberGenerator.GetBytes(32), pkg);

            result.ShouldBeOfType<Result<ApplyReport>.Fail>();
        }
        finally
        {
            Directory.Delete(repoA, true);
            Directory.Delete(repoB, true);
        }
    }

    [Fact]
    public void Apply_MaliciousPaths_RejectedNothingEscapes()
    {
        var repoB = TempDir.Create();
        try
        {
            TestGitRepo.Commit(repoB, "seed.txt", "seed\n", "seed");

            var manifest = new SyncManifest
            {
                SchemaVersion = 1,
                Channel = "slurpy",
                Mode = SyncMode.Git,
                BasePosition = null,
                NewPosition = new GitPosition(new string('a', 40)),
                Features = [],
                Commits = [],
                Files =
                [
                    new PackageFileEntry { Path = "../escaped.txt", Hash = "x", Content = "evil"u8.ToArray() },
                    new PackageFileEntry { Path = "/abs/evil.txt", Hash = "x", Content = "evil"u8.ToArray() }
                ],
                Deletions = ["../../victim.txt"]
            };
            var pkg = Path.Combine(repoB, "evil.synchr");
            PackageSealer.Seal(pkg, Key, manifest);

            var message = ApplyFail(repoB, GitState(), pkg);

            message.ShouldContain("not allowed");

            var escaped = Path.GetFullPath(Path.Combine(repoB, "../escaped.txt"));
            File.Exists(escaped).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(repoB, true);
            File.Delete(Path.GetFullPath(Path.Combine(repoB, "../escaped.txt")));
        }
    }

    [Fact]
    public void Apply_UnreadableLocalBase_FailsLoudInsteadOfSilentOverwrite()
    {
        var repoA = TestGitRepo.Create();
        var repoB = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(repoA, "a.txt", "from A\n", "add a");
            var headA = TestGitRepo.Commit(repoA, "b.txt", "bee from A\n", "add b");
            var headSeedB = TestGitRepo.Commit(repoB, "seed.txt", "seed\n", "seed");

            var baseline = PackGit(repoA, GitState(), full: true, Key, Path.Combine(repoA, "p0.synchr"));
            var bState = Apply(repoB, GitState(), baseline).UpdatedState!;

            var tamperedLedger = bState.Ledger.Select(e => e with { LocalPosition = new string('d', 40) }).ToList();
            var tampered = bState with { Ledger = tamperedLedger };

            TestGitRepo.Commit(repoA, "c.txt", "cee\n", "add c");
            var delta = PackGit(repoA, GitState(peerKnown: headA, peerHead: headSeedB), full: false, Key, Path.Combine(repoA, "p1.synchr"));

            var message = ApplyFail(repoB, tampered, delta);

            message.ShouldContain("git diff failed");
        }
        finally
        {
            Directory.Delete(repoA, true);
            Directory.Delete(repoB, true);
        }
    }

    [Fact]
    public void Apply_PendingScene_BlocksNextApply()
    {
        var repoA = TestGitRepo.Create();
        var repoB = TestGitRepo.Create();
        try
        {
            TestGitRepo.Commit(repoA, "a.txt", "from A\n", "add a");
            var headA = TestGitRepo.Commit(repoA, "b.txt", "bee from A\n", "add b");
            var headSeedB = TestGitRepo.Commit(repoB, "seed.txt", "seed\n", "seed");

            var baseline = PackGit(repoA, GitState(), full: true, Key, Path.Combine(repoA, "p0.synchr"));
            var bState = Apply(repoB, GitState(), baseline).UpdatedState!;

            TestGitRepo.Commit(repoA, "a.txt", "A rework\n", "a rework");
            TestGitRepo.Commit(repoB, "a.txt", "B rework\n", "b rework");
            var delta = PackGit(repoA, GitState(peerKnown: headA, peerHead: headSeedB), full: false, Key, Path.Combine(repoA, "p1.synchr"));
            var conflicted = Apply(repoB, bState, delta);

            var result = ApplyService.Apply(repoB, conflicted.UpdatedState!, Key, baseline);

            var fail = result.ShouldBeOfType<Result<ApplyReport>.Fail>();
            fail.Error.Message.ShouldContain("pending conflict scene");
        }
        finally
        {
            Directory.Delete(repoA, true);
            Directory.Delete(repoB, true);
        }
    }
}
