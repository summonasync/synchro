using Shouldly;
using Synchro.Core;
using Synchro.Core.Slave;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class AddSlaveServiceTests
{
    [Fact]
    public void Run_GitRepoPath_RegistersConfigVisibleThroughStoreApi()
    {
        using var area = new Area();
        var slave = TestGitRepo.Create();
        TestGitRepo.Commit(slave, "readme.md", "readme\n", "seed");

        var outcome = AddSlaveService.Run(area.Master, EmptyState(), slave, new ScriptedAddSlaveUi(name: "docs-slave"))
            .ShouldBeOfType<AddSlaveResult.Registered>();

        outcome.RepoBootstrapped.ShouldBeFalse();
        outcome.State.Slaves.ShouldContainKey("docs-slave");

        var stored = SlaveRegistry.Get(outcome.State, "docs-slave")
            .ShouldBeOfType<Result<SlaveConfig>.Ok>().Value;
        stored.Root.ShouldBe(slave);
        stored.SyncBranch.ShouldBe("feature-sync");
        stored.BaseBranch.ShouldBe("main");
        stored.CreatedAtUtc.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);

        HeadCount(slave).ShouldBe(1);
        GitCli.Run(slave, "branch", "--list", "feature-sync").Output.Trim().ShouldBeEmpty();
        GitCli.StatusPorcelain(slave).ShouldBeEmpty();
    }

    [Fact]
    public void Run_GitRepoPath_CustomBranchNames_AreRecordedVerbatim()
    {
        using var area = new Area();
        var slave = TestGitRepo.Create();
        TestGitRepo.Commit(slave, "readme.md", "readme\n", "seed");

        var outcome = AddSlaveService.Run(
                area.Master, EmptyState(), slave,
                new ScriptedAddSlaveUi(name: "ops-slave", syncBranch: "deploy-sync", baseBranch: "develop"))
            .ShouldBeOfType<AddSlaveResult.Registered>();

        outcome.Config.SyncBranch.ShouldBe("deploy-sync");
        outcome.Config.BaseBranch.ShouldBe("develop");
    }

    [Fact]
    public void Run_PlainFolder_WithConsent_BootstrapsAndRecordsHistory()
    {
        using var area = new Area();
        var plain = TempDir.Create();

        var outcome = AddSlaveService.Run(area.Master, EmptyState(), plain, new ScriptedAddSlaveUi(name: "fresh-slave"))
            .ShouldBeOfType<AddSlaveResult.Registered>();

        outcome.RepoBootstrapped.ShouldBeTrue();
        outcome.Config.Root.ShouldBe(plain);
        outcome.Config.BaseBranch.ShouldBe("main");

        GitCli.Run(plain, "rev-list", "--count", "HEAD").Output.Trim().ShouldBe("1");
        var log = GitCli.Run(plain, "log", "-1", "--pretty=%an%n%ae%n%s").Output;
        log.ShouldContain("synchro");
        log.ShouldContain("synchro@localhost");
        log.ShouldContain("synchro bootstrap");

        var history = SyncHistoryLog.List(outcome.State, "fresh-slave")
            .ShouldBeOfType<Result<IReadOnlyList<SyncHistoryRecord>>.Ok>().Value;
        history.Count.ShouldBe(1);
        history[0].Direction.ShouldBe(SyncDirection.MasterToSlave);
        history[0].FilesChanged.ShouldBe(0);
        history[0].Conflicts.ShouldBeEmpty();
        history[0].RolledBack.ShouldBeFalse();
    }

    [Fact]
    public void Run_PlainFolder_ConsentDeclined_FolderLeftUntouched()
    {
        using var area = new Area();
        var plain = TempDir.Create();
        File.WriteAllText(Path.Combine(plain, "keep.txt"), "keep\n");

        var outcome = AddSlaveService.Run(
                area.Master, EmptyState(), plain,
                new ScriptedAddSlaveUi(name: "shy-slave", consent: false))
            .ShouldBeOfType<AddSlaveResult.ConsentDeclined>();

        Directory.Exists(Path.Combine(plain, ".git")).ShouldBeFalse();
        File.ReadAllText(Path.Combine(plain, "keep.txt")).ShouldBe("keep\n");
    }

    [Fact]
    public void Run_DuplicateSlaveName_RefusedWithoutWrites()
    {
        using var area = new Area();
        var slave = TestGitRepo.Create();
        TestGitRepo.Commit(slave, "readme.md", "readme\n", "seed");
        var state = SlaveRegistry.Add(EmptyState(), ExistingConfig(slave))
            .ShouldBeOfType<Result<StoreState>.Ok>().Value;
        var before = HeadCount(slave);

        var outcome = AddSlaveService.Run(area.Master, state, slave, new ScriptedAddSlaveUi(name: "taken"))
            .ShouldBeOfType<AddSlaveResult.Refused>();

        outcome.Error.Code.ShouldBe("conflict");
        outcome.Error.Message.ShouldContain("already registered");
        HeadCount(slave).ShouldBe(before);
        GitCli.StatusPorcelain(slave).ShouldBeEmpty();
    }

    [Fact]
    public void Run_EmptySlaveName_RefusedWithoutWrites()
    {
        using var area = new Area();
        var plain = TempDir.Create();

        var outcome = AddSlaveService.Run(area.Master, EmptyState(), plain, new ScriptedAddSlaveUi(name: "   "))
            .ShouldBeOfType<AddSlaveResult.Refused>();

        outcome.Error.Code.ShouldBe("invalid");
        outcome.Error.Message.ShouldContain("slave name is required");
        Directory.Exists(Path.Combine(plain, ".git")).ShouldBeFalse();
    }

    [Fact]
    public void Run_NonExistentPath_Refused()
    {
        using var area = new Area();
        var missing = Path.Combine(area.Master, "nowhere");

        var outcome = AddSlaveService.Run(area.Master, EmptyState(), missing, new ScriptedAddSlaveUi())
            .ShouldBeOfType<AddSlaveResult.Refused>();

        outcome.Error.Code.ShouldBe("not_found");
        outcome.Error.Message.ShouldContain("does not exist");
    }

    private static int HeadCount(string repo) =>
        int.Parse(GitCli.Run(repo, "rev-list", "--count", "HEAD").Output.Trim());

    private static StoreState EmptyState() => new()
    {
        SchemaVersion = 1,
        Channel = new ChannelConfig { Name = "master", Peer = "peer", Mode = SyncMode.Git },
        Ledger = [],
        AppliedFileHashes = [],
        AppliedContents = []
    };

    private static SlaveConfig ExistingConfig(string root) => new()
    {
        Name = "taken",
        Root = root,
        SyncBranch = "feature-sync",
        BaseBranch = "main",
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private sealed class Area : IDisposable
    {
        public string Master { get; } = TestGitRepo.Create();

        public void Dispose() => Directory.Delete(Master, true);
    }
}
