using Shouldly;
using Synchro.Core;
using Synchro.Core.Store;
using Synchro.Core.Sync;
using Xunit;

namespace Synchro.Tests;

public class SyncSlaveResolverTests
{
    [Fact]
    public void Resolve_NoSlaves_FailsWithAddHint()
    {
        var resolved = SyncSlaveResolver.Resolve(EmptyState(), null);

        resolved.ShouldBeOfType<Result<SlaveConfig>.Fail>()
            .Error.Message.ShouldContain("add slave");
    }

    [Fact]
    public void Resolve_SingleRegistered_AutoSelected()
    {
        var state = SlaveRegistry.Add(EmptyState(), Slave("laptop")).ShouldBeOfType<Result<StoreState>.Ok>().Value;

        var resolved = SyncSlaveResolver.Resolve(state, null);

        resolved.ShouldBeOfType<Result<SlaveConfig>.Ok>().Value.Name.ShouldBe("laptop");
    }

    [Fact]
    public void Resolve_SeveralRegisteredWithoutName_FailsNamingThem()
    {
        var state = SlaveRegistry.Add(EmptyState(), Slave("laptop")).ShouldBeOfType<Result<StoreState>.Ok>().Value;
        state = SlaveRegistry.Add(state, Slave("khadgar")).ShouldBeOfType<Result<StoreState>.Ok>().Value;

        var resolved = SyncSlaveResolver.Resolve(state, null);

        var message = resolved.ShouldBeOfType<Result<SlaveConfig>.Fail>().Error.Message;
        message.ShouldContain("laptop");
        message.ShouldContain("khadgar");
    }

    [Fact]
    public void Resolve_NamedSlave_ReturnsIt()
    {
        var state = SlaveRegistry.Add(EmptyState(), Slave("laptop")).ShouldBeOfType<Result<StoreState>.Ok>().Value;
        state = SlaveRegistry.Add(state, Slave("khadgar")).ShouldBeOfType<Result<StoreState>.Ok>().Value;

        SyncSlaveResolver.Resolve(state, "khadgar")
            .ShouldBeOfType<Result<SlaveConfig>.Ok>().Value.Name.ShouldBe("khadgar");
    }

    [Fact]
    public void Resolve_UnknownName_FailsNotFound()
    {
        var state = SlaveRegistry.Add(EmptyState(), Slave("laptop")).ShouldBeOfType<Result<StoreState>.Ok>().Value;

        SyncSlaveResolver.Resolve(state, "ghost")
            .ShouldBeOfType<Result<SlaveConfig>.Fail>().Error.Message.ShouldContain("ghost");
    }

    private static StoreState EmptyState() => new()
    {
        SchemaVersion = 1,
        Channel = new ChannelConfig { Name = "master", Peer = "slave", Mode = SyncMode.Git },
        Ledger = [],
        AppliedFileHashes = [],
        AppliedContents = []
    };

    private static SlaveConfig Slave(string name) => new()
    {
        Name = name,
        Root = Path.Combine(Path.GetTempPath(), "synchro-slaves", name),
        SyncBranch = "feature-sync",
        BaseBranch = "main",
        CreatedAtUtc = DateTimeOffset.UtcNow
    };
}
