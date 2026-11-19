using System.Security.Cryptography;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class SlaveRegistryTests
{
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse("2026-11-20T10:00:00Z");

    private static string SlaveRoot(string name) => Path.Combine(Path.GetTempPath(), "synchro-slaves", name);

    private static StoreState EmptyState() => new()
    {
        SchemaVersion = 1,
        Channel = new ChannelConfig { Name = "master", Peer = "slave", Mode = SyncMode.Git },
        Ledger = [],
        AppliedFileHashes = new Dictionary<string, string>(),
        AppliedContents = new Dictionary<string, byte[]>()
    };

    private static SlaveConfig Slave(string name = "khadgar", string? root = null) => new()
    {
        Name = name,
        Root = root ?? SlaveRoot(name),
        SyncBranch = "feature-sync",
        BaseBranch = "main",
        CreatedAtUtc = CreatedAt
    };

    [Fact]
    public void Add_Get_RoundTripsThroughEncryptedContainer()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "container");
            var key = RandomNumberGenerator.GetBytes(32);
            var added = SlaveRegistry.Add(EmptyState(), Slave());
            var ok = added.ShouldBeOfType<Result<StoreState>.Ok>();
            StoreContainer.Save(path, key, ok.Value);

            var state = StoreContainer.Load(path, key).ShouldBeOfType<Result<StoreState>.Ok>().Value;
            var got = SlaveRegistry.Get(state, "khadgar").ShouldBeOfType<Result<SlaveConfig>.Ok>().Value;

            got.Root.ShouldBe(SlaveRoot("khadgar"));
            got.SyncBranch.ShouldBe("feature-sync");
            got.BaseBranch.ShouldBe("main");
            got.CreatedAtUtc.ShouldBe(CreatedAt);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Add_DuplicateName_RefusedWithConflict()
    {
        var state = SlaveRegistry.Add(EmptyState(), Slave()).ShouldBeOfType<Result<StoreState>.Ok>().Value;

        var again = SlaveRegistry.Add(state, Slave(root: SlaveRoot("elsewhere")));

        var fail = again.ShouldBeOfType<Result<StoreState>.Fail>();
        fail.Error.Code.ShouldBe("conflict");
        fail.Error.Message.ShouldContain("khadgar");
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData(" ")]
    [InlineData("")]
    public void Add_NonAbsoluteRoot_FailsWithInvalid(string root)
    {
        var added = SlaveRegistry.Add(EmptyState(), Slave(root: root));

        var fail = added.ShouldBeOfType<Result<StoreState>.Fail>();
        fail.Error.Code.ShouldBe("invalid");
        fail.Error.Message.ShouldContain("absolute path");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Add_BlankName_FailsWithInvalid(string name)
    {
        var added = SlaveRegistry.Add(EmptyState(), Slave(name: name));

        var fail = added.ShouldBeOfType<Result<StoreState>.Fail>();
        fail.Error.Code.ShouldBe("invalid");
        fail.Error.Message.ShouldContain("name");
    }

    [Theory]
    [InlineData("", "main")]
    [InlineData(" ", "main")]
    [InlineData("feature-sync", "")]
    [InlineData("feature-sync", " ")]
    public void Add_BlankBranch_FailsWithInvalid(string syncBranch, string baseBranch)
    {
        var slave = Slave() with { SyncBranch = syncBranch, BaseBranch = baseBranch };

        var added = SlaveRegistry.Add(EmptyState(), slave);

        var fail = added.ShouldBeOfType<Result<StoreState>.Fail>();
        fail.Error.Code.ShouldBe("invalid");
    }

    [Fact]
    public void Update_ExistingSlave_ReplacesFieldsKeepsCreatedTimestamp()
    {
        var state = SlaveRegistry.Add(EmptyState(), Slave()).ShouldBeOfType<Result<StoreState>.Ok>().Value;

        var updated = SlaveRegistry.Update(
            state, "khadgar", SlaveRoot("moved"), "inbox-sync", "develop");

        var ok = updated.ShouldBeOfType<Result<StoreState>.Ok>();
        var slave = SlaveRegistry.Get(ok.Value, "khadgar").ShouldBeOfType<Result<SlaveConfig>.Ok>().Value;
        slave.Root.ShouldBe(SlaveRoot("moved"));
        slave.SyncBranch.ShouldBe("inbox-sync");
        slave.BaseBranch.ShouldBe("develop");
        slave.CreatedAtUtc.ShouldBe(CreatedAt);
    }

    [Fact]
    public void Update_UnknownSlave_FailsWithNotFound()
    {
        var updated = SlaveRegistry.Update(EmptyState(), "ghost", SlaveRoot("ghost"), "feature-sync", "main");

        var fail = updated.ShouldBeOfType<Result<StoreState>.Fail>();
        fail.Error.Code.ShouldBe("not_found");
    }

    [Fact]
    public void Remove_ExistingSlave_DropsConfigHintAndHistory()
    {
        var registered = SlaveRegistry.Add(EmptyState(), Slave()).ShouldBeOfType<Result<StoreState>.Ok>().Value;
        var hinted = SlaveHints.Set(registered, "khadgar", [0x01, 0x02]).ShouldBeOfType<Result<StoreState>.Ok>().Value;
        var recorded = SyncHistoryLog.Append(hinted, "khadgar", HistoryRecord(1))
            .ShouldBeOfType<Result<StoreState>.Ok>().Value;

        var removed = SlaveRegistry.Remove(recorded, "khadgar");

        var ok = removed.ShouldBeOfType<Result<StoreState>.Ok>();
        ok.Value.Slaves.ShouldNotContainKey("khadgar");
        ok.Value.LedgerHints.ShouldNotContainKey("khadgar");
        ok.Value.SyncHistory.ShouldNotContainKey("khadgar");
    }

    [Fact]
    public void Remove_UnknownSlave_FailsWithNotFound()
    {
        var removed = SlaveRegistry.Remove(EmptyState(), "ghost");

        removed.ShouldBeOfType<Result<StoreState>.Fail>().Error.Code.ShouldBe("not_found");
    }

    [Fact]
    public void List_ReturnsSlavesOrderedByNameNotByInsertion()
    {
        var state = EmptyState();
        state = SlaveRegistry.Add(state, Slave("zeta")).ShouldBeOfType<Result<StoreState>.Ok>().Value;
        state = SlaveRegistry.Add(state, Slave("alpha")).ShouldBeOfType<Result<StoreState>.Ok>().Value;
        state = SlaveRegistry.Add(state, Slave("mid")).ShouldBeOfType<Result<StoreState>.Ok>().Value;

        var names = SlaveRegistry.List(state).Select(s => s.Name).ToArray();

        names.ShouldBe(["alpha", "mid", "zeta"]);
    }

    private static SyncHistoryRecord HistoryRecord(int files) => new()
    {
        AtUtc = CreatedAt,
        Direction = SyncDirection.MasterToSlave,
        FilesChanged = files,
        Conflicts = [],
        RolledBack = false
    };
}
