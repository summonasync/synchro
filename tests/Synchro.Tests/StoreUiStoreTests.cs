using Shouldly;
using Synchro.App.Ui;
using Synchro.Core;
using Synchro.Core.Landing;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class StoreUiStoreTests : IDisposable
{
    private readonly string _master;
    private readonly string _slave;
    private readonly string _container;
    private readonly byte[] _key;
    private readonly StoreUiStore _store;

    public StoreUiStoreTests()
    {
        _master = TestGitRepo.Create();
        _slave = TestGitRepo.Create();
        TestGitRepo.Commit(_master, "Readme.md", "master seed", "seed");
        TestGitRepo.Commit(_slave, "Readme.md", "slave seed", "seed");

        var init = StoreInitializer.Initialize(_master, new InitOptions());
        (init is Result<InitOutcome>.Ok).ShouldBeTrue();

        StoreInitializer.TryLoadKey(_master, out _key).ShouldBeTrue();
        _container = StorePaths.Container(_master);

        RegisterSlave("ui-test");
        _store = new StoreUiStore(_master);
    }

    [Fact]
    public void GetSlaves_ExposesRealSlaveConfigFromTheMasterStore()
    {
        var slaves = _store.GetSlaves();

        slaves.Count.ShouldBe(1);
        var slave = slaves.Single();
        slave.Name.ShouldBe("ui-test");
        slave.Path.ShouldBe(_slave);
        slave.SyncBranch.ShouldBe("feature-sync");
        slave.BaseBranch.ShouldBe("main");
        slave.SyncInFlight.ShouldBeFalse();
        slave.LastOperation.ShouldContain("none yet");
    }

    [Fact]
    public void GetSlaves_ReportsCleanInboxes_BeforeAnySync()
    {
        var slave = _store.GetSlaves().Single();

        slave.SlaveInbox.State.ShouldBe("clean");
        slave.SlaveInbox.Branch.ShouldBe("main");
        slave.SlaveInbox.DirtyFileCount.ShouldBe(0);
        slave.MasterInbox.State.ShouldBe("clean");
    }

    [Fact]
    public void GetSlaves_FlagsInFlight_WhenTheMarkerExists()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(InFlightMarker.PathFor(_master, "ui-test"))!);
        File.WriteAllText(InFlightMarker.PathFor(_master, "ui-test"), "stale");

        _store.GetSlaves().Single().SyncInFlight.ShouldBeTrue();
    }

    [Fact]
    public void GetHistory_IsEmptyUntilTheLedgerWriterRuns()
    {
        _store.GetHistory("ui-test").ShouldBeEmpty();
    }

    [Fact]
    public void GetHistory_ReadsRealLedgerEntriesNewestFirst()
    {
        AppendHistory(new SyncHistoryRecord
        {
            AtUtc = DateTimeOffset.Parse("2026-11-20 08:00:00Z"),
            Direction = SyncDirection.MasterToSlave,
            FilesChanged = 2,
            Conflicts = [],
            RolledBack = false,
        });
        AppendHistory(new SyncHistoryRecord
        {
            AtUtc = DateTimeOffset.Parse("2026-11-20 09:00:00Z"),
            Direction = SyncDirection.SlaveToMaster,
            FilesChanged = 1,
            Conflicts =
            [
                new SyncConflict { Path = "src/Program.cs", Winner = ConflictWinner.Slave },
            ],
            RolledBack = false,
        });

        var history = _store.GetHistory("ui-test");

        history.Count.ShouldBe(2);
        history[0].Direction.ShouldBe("slave → master");
        history[0].AtUtc.ShouldBeGreaterThanOrEqualTo(history[1].AtUtc);
        history[1].FileCount.ShouldBe(2);
        var resolution = history[0].ConflictResolution;
        resolution.ShouldNotBeNull();
        resolution.ShouldContain("src/Program.cs (slave wins)");
    }

    [Fact]
    public void GetConflicts_BuildsTheSceneFromResolvedHistoryConflicts()
    {
        AppendHistory(new SyncHistoryRecord
        {
            AtUtc = DateTimeOffset.Parse("2026-11-20 09:00:00Z"),
            Direction = SyncDirection.MasterToSlave,
            FilesChanged = 3,
            Conflicts =
            [
                new SyncConflict { Path = "docs/flow.md", Winner = ConflictWinner.Master },
            ],
            RolledBack = false,
        });

        var scene = _store.GetConflicts("ui-test");

        scene.ShouldNotBeNull();
        scene.Paths.Single().Path.ShouldBe("docs/flow.md");
        scene.Paths.Single().Resolution.ShouldBe("master-wins");
    }

    [Fact]
    public void UnknownSlavesAndMissingStoresFallBackToEmptyViews()
    {
        _store.GetHistory("nope").ShouldBeEmpty();
        _store.GetConflicts("nope").ShouldBeNull();

        var bare = new StoreUiStore(TestGitRepo.Create());
        bare.GetSlaves().ShouldBeEmpty();
        bare.GetHistory("whatever").ShouldBeEmpty();
    }

    private void RegisterSlave(string name)
    {
        var loaded = StoreContainer.Load(_container, _key);
        (loaded is Result<StoreState>.Ok).ShouldBeTrue();
        var state = ((Result<StoreState>.Ok)loaded).Value;

        state.Slaves[name] = new SlaveConfig
        {
            Name = name,
            Root = _slave,
            SyncBranch = "feature-sync",
            BaseBranch = "main",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        StoreContainer.Save(_container, _key, state);
    }

    private void AppendHistory(SyncHistoryRecord record)
    {
        var loaded = StoreContainer.Load(_container, _key);
        (loaded is Result<StoreState>.Ok).ShouldBeTrue();
        var state = ((Result<StoreState>.Ok)loaded).Value;

        var appended = SyncHistoryLog.Append(state, "ui-test", record);
        (appended is Result<StoreState>.Ok).ShouldBeTrue();

        StoreContainer.Save(_container, _key, ((Result<StoreState>.Ok)appended).Value);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_master, recursive: true);
            Directory.Delete(_slave, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
