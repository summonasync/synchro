using System.Security.Cryptography;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class SyncHistoryLogTests
{
    private static readonly DateTimeOffset Base = DateTimeOffset.Parse("2026-11-20T10:00:00Z");

    private static StoreState StateWithSlaves(params string[] names)
    {
        var slaves = names.ToDictionary(
            n => n,
            n => new SlaveConfig
            {
                Name = n,
                Root = Path.Combine(Path.GetTempPath(), "synchro-slaves", n),
                SyncBranch = "feature-sync",
                BaseBranch = "main",
                CreatedAtUtc = Base
            });
        return new StoreState
        {
            SchemaVersion = 1,
            Channel = new ChannelConfig { Name = "master", Peer = "slave", Mode = SyncMode.Git },
            Ledger = [],
            AppliedFileHashes = new Dictionary<string, string>(),
            AppliedContents = new Dictionary<string, byte[]>(),
            Slaves = slaves
        };
    }

    private static SyncHistoryRecord Record(
        int minutes, SyncDirection direction = SyncDirection.MasterToSlave, bool rolledBack = false) => new()
        {
            AtUtc = Base.AddMinutes(minutes),
            Direction = direction,
            FilesChanged = minutes,
            Conflicts = [],
            RolledBack = rolledBack
        };

    [Fact]
    public void Append_List_ReturnsNewestFirst()
    {
        var state = StateWithSlaves("khadgar");
        state = SyncHistoryLog.Append(state, "khadgar", Record(1)).ShouldBeOfType<Result<StoreState>.Ok>().Value;
        state = SyncHistoryLog.Append(state, "khadgar", Record(2)).ShouldBeOfType<Result<StoreState>.Ok>().Value;
        state = SyncHistoryLog.Append(state, "khadgar", Record(3)).ShouldBeOfType<Result<StoreState>.Ok>().Value;

        var listed = SyncHistoryLog.List(state, "khadgar").ShouldBeOfType<Result<IReadOnlyList<SyncHistoryRecord>>.Ok>().Value;

        listed.Select(r => r.AtUtc).ShouldBe([Base.AddMinutes(3), Base.AddMinutes(2), Base.AddMinutes(1)]);
    }

    [Fact]
    public void Append_SameTimestamp_ListsLastAppendedFirst()
    {
        var state = StateWithSlaves("khadgar");
        state = SyncHistoryLog.Append(state, "khadgar", Record(5)).ShouldBeOfType<Result<StoreState>.Ok>().Value;
        state = SyncHistoryLog.Append(state, "khadgar", Record(5)).ShouldBeOfType<Result<StoreState>.Ok>().Value;

        var listed = SyncHistoryLog.List(state, "khadgar").ShouldBeOfType<Result<IReadOnlyList<SyncHistoryRecord>>.Ok>().Value;

        listed.Count.ShouldBe(2);
        listed[0].FilesChanged.ShouldBe(5);
        listed[1].FilesChanged.ShouldBe(5);
        ReferenceEquals(listed[0], listed[1]).ShouldBeFalse();
    }

    [Fact]
    public void Append_List_PreservesOrderThroughEncryptedContainer()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "container");
            var key = RandomNumberGenerator.GetBytes(32);
            var state = StateWithSlaves("khadgar");
            state = SyncHistoryLog.Append(
                state, "khadgar", Record(1, SyncDirection.SlaveToMaster, rolledBack: true))
                .ShouldBeOfType<Result<StoreState>.Ok>().Value;
            state = SyncHistoryLog.Append(state, "khadgar", Record(2)).ShouldBeOfType<Result<StoreState>.Ok>().Value;
            StoreContainer.Save(path, key, state);

            var loaded = StoreContainer.Load(path, key).ShouldBeOfType<Result<StoreState>.Ok>().Value;
            var listed = SyncHistoryLog.List(loaded, "khadgar")
                .ShouldBeOfType<Result<IReadOnlyList<SyncHistoryRecord>>.Ok>().Value;

            listed.Count.ShouldBe(2);
            listed[0].AtUtc.ShouldBe(Base.AddMinutes(2));
            listed[0].Direction.ShouldBe(SyncDirection.MasterToSlave);
            listed[0].RolledBack.ShouldBeFalse();
            listed[1].AtUtc.ShouldBe(Base.AddMinutes(1));
            listed[1].Direction.ShouldBe(SyncDirection.SlaveToMaster);
            listed[1].RolledBack.ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Append_RecordWithConflicts_PreservesPerFileWinners()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "container");
            var key = RandomNumberGenerator.GetBytes(32);
            var record = Record(7) with
            {
                Conflicts =
                [
                    new SyncConflict { Path = "src/app.cs", Winner = ConflictWinner.Master },
                    new SyncConflict { Path = "docs/readme.md", Winner = ConflictWinner.Slave }
                ]
            };
            var state = SyncHistoryLog.Append(StateWithSlaves("khadgar"), "khadgar", record)
                .ShouldBeOfType<Result<StoreState>.Ok>().Value;
            StoreContainer.Save(path, key, state);

            var loaded = StoreContainer.Load(path, key).ShouldBeOfType<Result<StoreState>.Ok>().Value;
            var got = SyncHistoryLog.List(loaded, "khadgar")
                .ShouldBeOfType<Result<IReadOnlyList<SyncHistoryRecord>>.Ok>().Value
                .ShouldHaveSingleItem();

            got.Conflicts.Count.ShouldBe(2);
            got.Conflicts[0].Path.ShouldBe("src/app.cs");
            got.Conflicts[0].Winner.ShouldBe(ConflictWinner.Master);
            got.Conflicts[1].Path.ShouldBe("docs/readme.md");
            got.Conflicts[1].Winner.ShouldBe(ConflictWinner.Slave);
            got.FilesChanged.ShouldBe(7);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Append_UnknownSlave_FailsWithNotFound()
    {
        var appended = SyncHistoryLog.Append(StateWithSlaves("khadgar"), "ghost", Record(1));

        appended.ShouldBeOfType<Result<StoreState>.Fail>().Error.Code.ShouldBe("not_found");
    }

    [Fact]
    public void Append_NegativeFilesChanged_FailsWithInvalid()
    {
        var appended = SyncHistoryLog.Append(StateWithSlaves("khadgar"), "khadgar", Record(-1));

        var fail = appended.ShouldBeOfType<Result<StoreState>.Fail>();
        fail.Error.Code.ShouldBe("invalid");
        fail.Error.Message.ShouldContain("files-changed");
    }

    [Fact]
    public void List_SlaveWithoutRecords_ReturnsEmpty()
    {
        var listed = SyncHistoryLog.List(StateWithSlaves("khadgar"), "khadgar");

        listed.ShouldBeOfType<Result<IReadOnlyList<SyncHistoryRecord>>.Ok>().Value.ShouldBeEmpty();
    }

    [Fact]
    public void List_UnknownSlave_FailsWithNotFound()
    {
        SyncHistoryLog.List(StateWithSlaves("khadgar"), "ghost")
            .ShouldBeOfType<Result<IReadOnlyList<SyncHistoryRecord>>.Fail>().Error.Code.ShouldBe("not_found");
    }

    [Fact]
    public void History_IsIsolatedPerSlave()
    {
        var state = StateWithSlaves("khadgar", "lothur");
        state = SyncHistoryLog.Append(state, "khadgar", Record(1)).ShouldBeOfType<Result<StoreState>.Ok>().Value;
        state = SyncHistoryLog.Append(state, "lothur", Record(2)).ShouldBeOfType<Result<StoreState>.Ok>().Value;

        var khadgar = SyncHistoryLog.List(state, "khadgar")
            .ShouldBeOfType<Result<IReadOnlyList<SyncHistoryRecord>>.Ok>().Value;
        var lothur = SyncHistoryLog.List(state, "lothur")
            .ShouldBeOfType<Result<IReadOnlyList<SyncHistoryRecord>>.Ok>().Value;

        khadgar.ShouldHaveSingleItem().FilesChanged.ShouldBe(1);
        lothur.ShouldHaveSingleItem().FilesChanged.ShouldBe(2);
    }
}
