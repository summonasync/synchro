using System.Security.Cryptography;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class StoreContainerTests
{
    private static readonly byte[] Key =
    [
        0x2b, 0x7e, 0x15, 0x16, 0x28, 0xae, 0xd2, 0xa6,
        0xab, 0xf7, 0x15, 0x88, 0x09, 0xcf, 0x4f, 0x3c,
        0xef, 0x43, 0x59, 0x1f, 0x67, 0x40, 0x22, 0x61,
        0x77, 0xdd, 0x02, 0x2e, 0x51, 0x66, 0x23, 0x11
    ];

    private static StoreState SampleState() => new()
    {
        SchemaVersion = 1,
        Channel = new ChannelConfig { Name = "slurpy", Peer = "shiva", Mode = SyncMode.Git },
        Ledger =
        [
            new LedgerEvent
            {
                AtUtc = DateTimeOffset.Parse("2026-11-10T12:00:00Z"),
                Direction = LedgerDirection.Sent,
                LocalPosition = "abc123",
                PeerPosition = "def456",
                PackageDigest = "deadbeef"
            }
        ],
        AppliedFileHashes = new Dictionary<string, string> { ["src/app.cs"] = "aa11" },
        AppliedContents = new Dictionary<string, byte[]> { ["src/app.cs"] = [0x01, 0x02] }
    };

    [Fact]
    public void Load_SavedContainer_ReturnsOriginalState()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "container");
            var state = SampleState();

            StoreContainer.Save(path, Key, state);
            var loaded = StoreContainer.Load(path, Key);

            var ok = loaded.ShouldBeOfType<Result<StoreState>.Ok>();
            ok.Value.Channel.Name.ShouldBe("slurpy");
            ok.Value.Channel.Peer.ShouldBe("shiva");
            ok.Value.Channel.Mode.ShouldBe(SyncMode.Git);
            ok.Value.Ledger.ShouldHaveSingleItem().Direction.ShouldBe(LedgerDirection.Sent);
            ok.Value.AppliedFileHashes["src/app.cs"].ShouldBe("aa11");
            ok.Value.AppliedContents["src/app.cs"].ShouldBe([0x01, 0x02]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ContainerBytes_LeakNeitherMagicNorPlaintext()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "container");
            var key = RandomNumberGenerator.GetBytes(32);
            StoreContainer.Save(path, key, SampleState());
            var bytes = File.ReadAllBytes(path);

            var utf8 = System.Text.Encoding.UTF8.GetString(bytes);
            var utf16Le = System.Text.Encoding.Unicode.GetString(bytes);

            utf8.ShouldNotContain("slurpy");
            utf8.ShouldNotContain("shiva");
            utf16Le.ShouldNotContain("slurpy");
            bytes.Length.ShouldBeGreaterThan(64);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Save_SameStateTwice_ProducesDifferentBytes()
    {
        var dir = TempDir.Create();
        try
        {
            var pathA = Path.Combine(dir, "a");
            var pathB = Path.Combine(dir, "b");
            var key = RandomNumberGenerator.GetBytes(32);

            StoreContainer.Save(pathA, key, SampleState());
            StoreContainer.Save(pathB, key, SampleState());

            var bytesA = File.ReadAllBytes(pathA);
            var bytesB = File.ReadAllBytes(pathB);
            bytesA.SequenceEqual(bytesB).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_WrongKey_ReturnsCleanFailure()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "container");
            StoreContainer.Save(path, Key, SampleState());

            var loaded = StoreContainer.Load(path, RandomNumberGenerator.GetBytes(32));

            var fail = loaded.ShouldBeOfType<Result<StoreState>.Fail>();
            fail.Error.Code.ShouldBe("invalid");
            fail.Error.Message.ShouldContain("wrong key");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_UnsupportedSchemaVersion_FailsCleanly()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "container");
            var future = SampleState() with { SchemaVersion = 99 };
            StoreContainer.Save(path, Key, future);

            var loaded = StoreContainer.Load(path, Key);

            var fail = loaded.ShouldBeOfType<Result<StoreState>.Fail>();
            fail.Error.Message.ShouldContain("schema version");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_TruncatedContainer_ReturnsCleanFailure()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "container");
            File.WriteAllBytes(path, [0x01, 0x02, 0x03]);

            var loaded = StoreContainer.Load(path, Key);

            loaded.ShouldBeOfType<Result<StoreState>.Fail>();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_TamperedContainerByte_FailsCleanly()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "container");
            var state = SampleState() with
            {
                SyncHistory = new Dictionary<string, IReadOnlyList<SyncHistoryRecord>>
                {
                    ["khadgar"] =
                    [
                        new SyncHistoryRecord
                        {
                            AtUtc = DateTimeOffset.Parse("2026-11-20T10:00:00Z"),
                            Direction = SyncDirection.MasterToSlave,
                            FilesChanged = 2,
                            Conflicts = [],
                            RolledBack = false
                        }
                    ]
                }
            };
            StoreContainer.Save(path, Key, state);

            var bytes = File.ReadAllBytes(path);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(path, bytes);

            var loaded = StoreContainer.Load(path, Key);

            var fail = loaded.ShouldBeOfType<Result<StoreState>.Fail>();
            fail.Error.Code.ShouldBe("invalid");
            fail.Error.Message.ShouldContain("corrupted");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public static class TempDir
{
    public static string Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "synchro-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
