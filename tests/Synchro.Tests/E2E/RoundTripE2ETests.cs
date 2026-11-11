using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Apply;
using Synchro.Core.Package;
using Synchro.Core.Pack;
using Synchro.Core.Scene;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests.E2E;

public class RoundTripE2ETests
{
    private static readonly byte[] ChannelKey = RandomNumberGenerator.GetBytes(32);

    private sealed class Side : IDisposable
    {
        public string Dir { get; }
        public StoreState State { get; set; }

        public Side(string dir, StoreState initialState)
        {
            Dir = dir;
            State = initialState;
        }

        public void Dispose() => Directory.Delete(Dir, true);

        public string Pack(bool full = false, byte[]? key = null)
        {
            var draftResult = PackService.Pack(Dir, State, new PackOptions { Full = full });
            if (draftResult is not Result<PackDraft>.Ok okDraft)
            {
                throw new InvalidOperationException("PACK FAILED: " + ((Result<PackDraft>.Fail)draftResult).Error.Message);
            }

            var draft = okDraft.Value;
            var path = Path.Combine(Dir, $"pkg-{Guid.NewGuid():N}.synchr");
            PackageSealer.Seal(path, key ?? ChannelKey, draft.Manifest);
            if (draft.UpdatedState is { } updated)
            {
                State = updated;
            }

            return path;
        }

        public ApplyReport Apply(string packagePath, byte[]? key = null)
        {
            var probe = ApplyService.Apply(Dir, State, key ?? ChannelKey, packagePath);
            if (probe is not Result<ApplyReport>.Ok okProbe)
            {
                throw new InvalidOperationException("APPLY FAILED: " + ((Result<ApplyReport>.Fail)probe).Error.Message);
            }

            var report = okProbe.Value;
            if (report.UpdatedState is { } updated)
            {
                State = updated;
            }

            return report;
        }

        public SyncError FailError(string packagePath, byte[]? key = null) =>
            ApplyService.Apply(Dir, State, key ?? ChannelKey, packagePath)
                .ShouldBeOfType<Result<ApplyReport>.Fail>().Error;

        public SyncError FailError(string packagePath, StoreState state) =>
            ApplyService.Apply(Dir, state, ChannelKey, packagePath)
                .ShouldBeOfType<Result<ApplyReport>.Fail>().Error;

        public void Write(string path, string content)
        {
            var full = Path.Combine(Dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        public string Read(string path) => File.ReadAllText(Path.Combine(Dir, path));

    }

    public enum SideMode { Git, Plain }

    private static Side GitSide()
    {
        var dir = TestGitRepo.Create();
        TestGitRepo.Commit(dir, "seed.txt", "seed\n", "seed");
        var state = new StoreState
        {
            SchemaVersion = 1,
            Channel = new ChannelConfig { Name = "channel", Peer = "peer", Mode = SyncMode.Git },
            Ledger = [],
            AppliedFileHashes = [],
            AppliedContents = []
        };
        return new Side(dir, state);
    }

    private static Side PlainSide()
    {
        var dir = TempDir.Create();
        var state = new StoreState
        {
            SchemaVersion = 1,
            Channel = new ChannelConfig { Name = "channel", Peer = "peer", Mode = SyncMode.Plain },
            Ledger = [],
            AppliedFileHashes = [],
            AppliedContents = []
        };
        return new Side(dir, state);
    }

    private static void AssertLedgerInvariants(Side side)
    {
        foreach (var evt in side.State.Ledger.Where(e => e.Direction == LedgerDirection.Applied))
        {
            evt.PackageDigest.ShouldNotBeNull();
            evt.PeerPosition.ShouldNotBeNull();
            evt.AtUtc.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);
        }

        if (side.State.Channel.Mode == SyncMode.Plain)
        {
            side.State.AppliedChainSeq.ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public void RoundTrip_GitGit_DivergentWork_WithConflictAndResolution()
    {
        using var dev = GitSide();
        TestGitRepo.Commit(dev.Dir, "lib/core.cs", "core v1\n", "core");
        var baselineHead = TestGitRepo.Commit(dev.Dir, "lib/readme.md", "readme\n", "readme");
        var baseline = dev.Pack(full: true);

        using var client = GitSide();
        client.Apply(baseline);
        client.Read("lib/core.cs").ShouldBe("core v1\n");

        TestGitRepo.Commit(dev.Dir, "lib/core.cs", "core v2 dev\n", "dev core rework");
        TestGitRepo.Commit(dev.Dir, "lib/extra.cs", "extra\n", "dev extra");
        TestGitRepo.Commit(client.Dir, "lib/core.cs", "core v2 client\n", "client core rework");
        var clientPackResult = PackService.Pack(client.Dir, client.State, new PackOptions { Full = true });
        if (clientPackResult is not Result<PackDraft>.Ok cfOk)
        {
            throw new InvalidOperationException("client full pack failed: " + ((Result<PackDraft>.Fail)clientPackResult).Error.Message);
        }

        var clientFullPath = Path.Combine(client.Dir, "client-full.synchr");
        PackageSealer.Seal(clientFullPath, ChannelKey, cfOk.Value.Manifest);

        var report = dev.Apply(clientFullPath);
        report.ScenePending.ShouldBeTrue();
        report.ConflictPaths.ShouldBe(["lib/core.cs"]);
        report.AppliedFiles.ShouldNotContain("lib/core.cs");

        dev.State.PendingScene.ShouldNotBeNull();
        dev.Write("lib/core.cs", "core v2 client\n");
        var resolved = SceneService.Resolve(dev.Dir, dev.State, "lib/core.cs", SceneService.OpReplace)
            .ShouldBeOfType<Result<StoreState>.Ok>().Value;
        var committed = SceneService.Commit(dev.Dir, resolved, ChannelKey)
            .ShouldBeOfType<Result<ApplyReport>.Ok>().Value;
        dev.State = committed.UpdatedState!;

        TestGitRepo.Commit(dev.Dir, "lib/resolved.txt", "resolved\n", "commit resolved state");
        var devState = committed.UpdatedState!;
        devState.PeerKnownLocalPosition.ShouldNotBeNull();

        var delta = dev.Pack();
        var clientDeltaResult = ApplyService.Apply(client.Dir, client.State, ChannelKey, delta);
        if (clientDeltaResult is not Result<ApplyReport>.Ok clientOk)
        {
            throw new InvalidOperationException("client delta apply failed: " + ((Result<ApplyReport>.Fail)clientDeltaResult).Error.Message
                + " | deltaBase=" + (dev.State.PeerKnownLocalPosition ?? "null")
                + " | clientPeerHead=" + (client.State.PeerHeadPosition ?? "null"));
        }
        var clientReport = clientOk.Value;
        if (clientReport.ScenePending)
        {
            // dev's replace chose the dev fork; the delta carries it back and the client's
            // local rework now diverges — an honest conflict on redelivery
            clientReport.ConflictPaths.ShouldBe(["lib/core.cs"]);
            client.Read("lib/core.cs").ShouldBe("core v2 client\n");
            client.Read("lib/extra.cs").ShouldBe("extra\n");
        }

        AssertLedgerInvariants(dev);
        AssertLedgerInvariants(client);
    }

    [Fact]
    public void RoundTrip_PlainPlain_FullCycle_WithReturnTrip()
    {
        using var dev = PlainSide();
        dev.Write("docs/spec.md", "spec v1\n");
        var baseline = dev.Pack();

        using var client = PlainSide();
        client.Apply(baseline);
        client.Read("docs/spec.md").ShouldBe("spec v1\n");
        AssertLedgerInvariants(client);

        // identity pack confirms the chain: dev can validate the next delta against it
        var identityTrip = client.Pack();
        dev.Apply(identityTrip);

        client.Write("docs/spec.md", "spec v2 by client\n");
        client.Write("docs/client-notes.md", "client notes\n");
        var returnTrip = client.Pack();

        var applied = dev.Apply(returnTrip);
        dev.Read("docs/spec.md").ShouldBe("spec v2 by client\n");
        dev.Read("docs/client-notes.md").ShouldBe("client notes\n");
        AssertLedgerInvariants(dev);
    }

    [Theory]
    [InlineData(SideMode.Git, SideMode.Git)]
    [InlineData(SideMode.Plain, SideMode.Git)]
    [InlineData(SideMode.Plain, SideMode.Plain)]
    public void RoundTrip_Matrix_BaselineAppliesAcrossModePairs(SideMode sender, SideMode receiver)
    {
        using var a = sender == SideMode.Git ? GitSide() : PlainSide();
        a.Write("payload/data.txt", "payload\n");
        if (sender == SideMode.Git)
        {
            TestGitRepo.Commit(a.Dir, "payload/data.txt", "payload\n", "payload");
        }

        var baseline = a.Pack(full: true);

        using var b = receiver == SideMode.Git ? GitSide() : PlainSide();
        var report = b.Apply(baseline);

        b.Read("payload/data.txt").ShouldBe("payload\n");
        report.AppliedFiles.ShouldBe(sender == SideMode.Git
            ? ["payload/data.txt", "seed.txt"]
            : ["payload/data.txt"]);
        AssertLedgerInvariants(b);
    }

    [Fact]
    public void DriftWithoutOverlap_AppliesSilently_KeepsLocalEdit()
    {
        using var dev = PlainSide();
        dev.Write("shared.txt", "shared v1\n");
        var baseline = dev.Pack();

        using var client = PlainSide();
        client.Apply(baseline);
        client.Write("local-only.txt", "my local note\n");
        var clientAfterOwnPack = client.Pack();

        dev.Write("dev-only.txt", "dev note\n");
        var devDelta = dev.Pack();

        var report = client.Apply(devDelta);
        if (report.AppliedFiles.Count == 0)
        {
            throw new InvalidOperationException(System.Text.Json.JsonSerializer.Serialize(new
            {
                report.ScenePending,
                report.ConflictPaths,
                report.Warnings,
                manifestFiles = "dev-only.txt",
                clientTree = Directory.GetFiles(client.Dir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(client.Dir, f))
            }));
        }

        report.ScenePending.ShouldBeFalse();
        report.AppliedFiles.ShouldBe(["dev-only.txt"]);
        client.Read("local-only.txt").ShouldBe("my local note\n");
        client.Read("dev-only.txt").ShouldBe("dev note\n");
        AssertLedgerInvariants(client);
    }

    [Fact]
    public void DriftWithOverlap_Plain_RoutesToSceneAndKeepsLocal()
    {
        using var dev = PlainSide();
        dev.Write("a.txt", "v1\n");
        dev.Write("b.txt", "untouched\n");
        var baseline = dev.Pack(full: true);

        using var client = PlainSide();
        client.Apply(baseline);
        client.Write("a.txt", "local v1\n");
        var localState = client.State;

        dev.Write("a.txt", "remote v2\n");
        dev.Write("b.txt", "remote b\n");
        var delta = dev.Pack();

        var report = client.Apply(delta);

        report.ScenePending.ShouldBeTrue();
        report.ConflictPaths.ShouldBe(["a.txt"]);
        report.AppliedFiles.ShouldBe(["b.txt"]);
        client.Read("a.txt").ShouldBe("local v1\n");
        client.Read("b.txt").ShouldBe("remote b\n");
        AssertLedgerInvariants(client);
    }

    [Fact]
    public void OutOfOrder_Plain_SkippedChainPosition_IsRejectedWithGuidance()
    {
        using var dev = PlainSide();
        dev.Write("a.txt", "a v1\n");
        var p1 = dev.Pack();

        using var client = PlainSide();
        client.Apply(p1);
        var clientAt1 = client.State;

        dev.Write("a.txt", "a v2\n");
        var p2 = dev.Pack();
        dev.Write("a.txt", "a v3\n");
        var p3 = dev.Pack();

        var error = client.FailError(p3, clientAt1);

        error.Code.ShouldBe("conflict");
        error.Message.ShouldContain("out-of-order or lost package");
        error.Message.ShouldContain("#1");
    }

    [Fact]
    public void OutOfOrder_Git_DeltaBeforeBaseline_IsRejectedWithGuidance()
    {
        using var dev = GitSide();
        TestGitRepo.Commit(dev.Dir, "a.txt", "v1\n", "one");
        var headA = TestGitRepo.Commit(dev.Dir, "a.txt", "v2\n", "two");

        var manifest = new SyncManifest
        {
            SchemaVersion = 1,
            Channel = "channel",
            Mode = SyncMode.Git,
            BasePosition = new GitPosition(headA),
            NewPosition = new GitPosition(new string('b', 40)),
            Features = [],
            Commits = [],
            Files = [new PackageFileEntry { Path = "a.txt", Hash = "x", Content = "v2\n"u8.ToArray() }],
            Deletions = []
        };
        var delta = Path.Combine(dev.Dir, "delta.synchr");
        PackageSealer.Seal(delta, ChannelKey, manifest);

        using var client = GitSide();
        var error = client.FailError(delta);

        error.Code.ShouldBe("conflict");
        error.Message.ShouldContain("no baseline applied yet");
    }

    [Fact]
    public void LostPackage_Git_BaseAheadOfClientKnowledge_IsRejectedAsOutOfOrder()
    {
        using var dev = GitSide();
        TestGitRepo.Commit(dev.Dir, "a.txt", "v1\n", "one");
        var baseline = dev.Pack(full: true);

        using var client = GitSide();
        client.Apply(baseline);
        var clientState = client.State;

        TestGitRepo.Commit(dev.Dir, "a.txt", "v2\n", "two");
        var firstCommit = TestGitRepo.Commit(dev.Dir, "lost.txt", "lost\n", "lost package");
        dev.State = dev.State with { PeerKnownLocalPosition = firstCommit };
        var fromUnknownBase = dev.Pack();

        var error = client.FailError(fromUnknownBase, clientState);

        error.Code.ShouldBe("conflict");
        error.Message.ShouldContain("out-of-order or lost package");
    }

    [Fact]
    public void DoubleApply_BothModes_AreFriendlyNoOps()
    {
        using var gitDev = GitSide();
        TestGitRepo.Commit(gitDev.Dir, "g.txt", "g\n", "one");
        var gitPkg = gitDev.Pack(full: true);

        using var gitClient = GitSide();
        gitClient.Apply(gitPkg);
        var gitAgain = gitClient.Apply(gitPkg);
        gitAgain.AlreadyApplied.ShouldBeTrue();

        using var plainDev = PlainSide();
        plainDev.Write("p.txt", "p\n");
        var plainPkg = plainDev.Pack();

        using var plainClient = PlainSide();
        plainClient.Apply(plainPkg);
        var plainAgain = plainClient.Apply(plainPkg);
        plainAgain.AlreadyApplied.ShouldBeTrue();
    }

    [Fact]
    public void CorruptPackage_TruncatedMidBytes_FailsCleanly()
    {
        using var dev = PlainSide();
        dev.Write("a.txt", "a\n");
        var pkg = dev.Pack();

        var raw = File.ReadAllBytes(pkg);
        File.WriteAllBytes(pkg, raw[..(raw.Length / 2)]);

        using var client = PlainSide();
        var error = client.FailError(pkg);

        error.Message.ShouldContain("wrong key or corrupted");
    }

    [Fact]
    public void WrongKey_FailsCleanlyWithoutTouchingPackage()
    {
        using var dev = PlainSide();
        dev.Write("p.txt", "p\n");
        var pkg = dev.Pack();
        var pkgBytes = File.ReadAllBytes(pkg);

        using var client = PlainSide();
        var error = client.FailError(pkg, RandomNumberGenerator.GetBytes(32));

        error.Message.ShouldContain("wrong key");
        File.ReadAllBytes(pkg).ShouldBe(pkgBytes);
    }

    [Fact]
    public void PackageAndStoreBytes_CarryNoMachineIdentifiers()
    {
        using var dev = PlainSide();
        StoreInitializer.Initialize(dev.Dir, new InitOptions { Peer = "peer" })
            .ShouldBeOfType<Result<InitOutcome>.Ok>();
        dev.Write("a.txt", "content\n");
        var pkg = dev.Pack();

        var markers = new[]
        {
            Environment.MachineName,
            Environment.UserName,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                .Split('/', '\\').LastOrDefault(s => s.Length > 3) ?? "home",
            Path.GetFileName(dev.Dir.TrimEnd('/'))
        }.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.ToLowerInvariant()).Distinct().ToList();

        var packageText = Encoding.UTF8.GetString(File.ReadAllBytes(pkg)).ToLowerInvariant();
        var storeText = Encoding.UTF8.GetString(File.ReadAllBytes(
            Path.Combine(dev.Dir, ".synchro", "container"))).ToLowerInvariant();

        foreach (var marker in markers)
        {
            packageText.ShouldNotContain(marker);
            storeText.ShouldNotContain(marker);
        }

        Regex.IsMatch(packageText, @"[a-z]:\\").ShouldBeFalse();
        Regex.IsMatch(storeText, @"[a-z]:\\").ShouldBeFalse();
    }
}
