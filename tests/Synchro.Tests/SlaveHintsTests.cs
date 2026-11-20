using System.Security.Cryptography;
using System.Text;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class SlaveHintsTests
{
    private static readonly byte[] Key =
    [
        0x11, 0x01, 0x8d, 0x41, 0x90, 0x2e, 0xf2, 0xa5,
        0x6b, 0xc3, 0x15, 0x87, 0x09, 0xaf, 0x3f, 0x2c,
        0xe1, 0x53, 0x21, 0x67, 0x40, 0x22, 0x61, 0x77,
        0x0d, 0x8d, 0x92, 0x1e, 0x51, 0x46, 0x13, 0x91
    ];

    private static StoreState StateWithSlave(string name) => new()
    {
        SchemaVersion = 1,
        Channel = new ChannelConfig { Name = "master", Peer = "slave", Mode = SyncMode.Git },
        Ledger = [],
        AppliedFileHashes = new Dictionary<string, string>(),
        AppliedContents = new Dictionary<string, byte[]>(),
        Slaves = new Dictionary<string, SlaveConfig>
        {
            [name] = new SlaveConfig
            {
                Name = name,
                Root = Path.Combine(Path.GetTempPath(), "synchro-slaves", name),
                SyncBranch = "feature-sync",
                BaseBranch = "main",
                CreatedAtUtc = DateTimeOffset.Parse("2026-11-20T10:00:00Z")
            }
        }
    };

    [Fact]
    public void Set_Get_RoundTripsThroughEncryptedContainer()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "container");
            var payload = Encoding.UTF8.GetBytes("{\"commits\":[\"abc123\"]}");
            var state = SlaveHints.Set(StateWithSlave("khadgar"), "khadgar", payload)
                .ShouldBeOfType<Result<StoreState>.Ok>().Value;
            StoreContainer.Save(path, Key, state);

            var loaded = StoreContainer.Load(path, Key).ShouldBeOfType<Result<StoreState>.Ok>().Value;
            var got = SlaveHints.Get(loaded, "khadgar").ShouldBeOfType<Result<byte[]>.Ok>().Value;

            got.ShouldBe(payload);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Set_ReplacesExistingHint_ReadBackCarriesOnlyNewBlob()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "container");
            var state = SlaveHints.Set(StateWithSlave("khadgar"), "khadgar", [0x01])
                .ShouldBeOfType<Result<StoreState>.Ok>().Value;
            var replaced = SlaveHints.Set(state, "khadgar", [0xAA, 0xBB, 0xCC])
                .ShouldBeOfType<Result<StoreState>.Ok>().Value;
            StoreContainer.Save(path, Key, replaced);

            var loaded = StoreContainer.Load(path, Key).ShouldBeOfType<Result<StoreState>.Ok>().Value;
            SlaveHints.Get(loaded, "khadgar").ShouldBeOfType<Result<byte[]>.Ok>().Value.ShouldBe([0xAA, 0xBB, 0xCC]);
            Directory.GetFiles(dir, "*.tmp-*").ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Set_UnknownSlave_FailsWithNotFound()
    {
        var set = SlaveHints.Set(StateWithSlave("khadgar"), "ghost", [0x01]);

        set.ShouldBeOfType<Result<StoreState>.Fail>().Error.Code.ShouldBe("not_found");
    }

    [Fact]
    public void Get_SlaveWithoutHint_FailsWithNotFound()
    {
        var got = SlaveHints.Get(StateWithSlave("khadgar"), "khadgar");

        var fail = got.ShouldBeOfType<Result<byte[]>.Fail>();
        fail.Error.Code.ShouldBe("not_found");
        fail.Error.Message.ShouldContain("no ledger hint");
    }

    [Theory]
    [InlineData(new byte[0])]
    public void Set_EmptyPayload_FailsWithInvalid(byte[] payload)
    {
        var set = SlaveHints.Set(StateWithSlave("khadgar"), "khadgar", payload);

        set.ShouldBeOfType<Result<StoreState>.Fail>().Error.Code.ShouldBe("invalid");
    }

    [Fact]
    public void CorruptBlobForOneSlave_IsolatedFromOtherSlavesData()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "container");
            var twoSlaves = SlaveRegistry.Add(
                StateWithSlave("khadgar"), Slave("lothur")).ShouldBeOfType<Result<StoreState>.Ok>().Value;
            byte[] garbage = [0xFF, 0x00, 0xDE, 0xAD, 0x2F];
            var state = SlaveHints.Set(twoSlaves, "khadgar", garbage).ShouldBeOfType<Result<StoreState>.Ok>().Value;
            var json = Encoding.UTF8.GetBytes("{\"tip\":\"9abc\"}");
            state = SlaveHints.Set(state, "lothur", json).ShouldBeOfType<Result<StoreState>.Ok>().Value;
            state = SyncHistoryLog.Append(state, "khadgar", Record())
                .ShouldBeOfType<Result<StoreState>.Ok>().Value;
            StoreContainer.Save(path, Key, state);

            var loaded = StoreContainer.Load(path, Key).ShouldBeOfType<Result<StoreState>.Ok>().Value;
            SlaveHints.Get(loaded, "khadgar").ShouldBeOfType<Result<byte[]>.Ok>().Value.ShouldBe(garbage);
            SlaveHints.Get(loaded, "lothur").ShouldBeOfType<Result<byte[]>.Ok>().Value.ShouldBe(json);
            SyncHistoryLog.List(loaded, "khadgar").ShouldBeOfType<Result<IReadOnlyList<SyncHistoryRecord>>.Ok>()
                .Value.ShouldHaveSingleItem().FilesChanged.ShouldBe(3);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static SlaveConfig Slave(string name) => new()
    {
        Name = name,
        Root = Path.Combine(Path.GetTempPath(), "synchro-slaves", name),
        SyncBranch = "feature-sync",
        BaseBranch = "main",
        CreatedAtUtc = DateTimeOffset.Parse("2026-11-20T10:00:00Z")
    };

    private static SyncHistoryRecord Record() => new()
    {
        AtUtc = DateTimeOffset.Parse("2026-11-20T10:05:00Z"),
        Direction = SyncDirection.MasterToSlave,
        FilesChanged = 3,
        Conflicts = [],
        RolledBack = false
    };
}
