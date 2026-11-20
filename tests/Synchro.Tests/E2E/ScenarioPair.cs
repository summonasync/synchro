using System.Security.Cryptography;
using System.Text;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Slave;
using Synchro.Core.Store;
using Synchro.Core.Sync;

namespace Synchro.Tests.E2E;

public static class ScenarioPair
{
    public const string SlaveName = "laptop";

    public static StoreState RegisterSlave(string masterRoot, string slaveRoot, string name = SlaveName)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        StoreInitializer.Initialize(masterRoot, new InitOptions { KeyHex = Convert.ToHexStringLower(key) })
            .ShouldBeOfType<Result<InitOutcome>.Ok>();
        var state = StoreContainer.Load(StorePaths.Container(masterRoot), key)
            .ShouldBeOfType<Result<StoreState>.Ok>().Value;
        return AddSlaveService.Run(masterRoot, state, slaveRoot, new ScriptedAddSlaveUi(name: name))
            .ShouldBeOfType<AddSlaveResult.Registered>().State;
    }

    public static StoreState EmptyState() => new()
    {
        SchemaVersion = 1,
        Channel = new ChannelConfig { Name = "master", Peer = "peer", Mode = SyncMode.Git },
        Ledger = [],
        AppliedFileHashes = [],
        AppliedContents = []
    };

    public static SyncRunOutput Sync(
        string masterRoot,
        StoreState state,
        ISyncUi? ui = null,
        bool nonInteractive = true)
    {
        var slave = SlaveRegistry.List(state).Single();
        return SyncOrchestrator.Run(masterRoot, state, slave, ui ?? new ScriptedSyncUi(), nonInteractive)
            .ShouldBeOfType<Result<SyncRunOutput>.Ok>().Value;
    }

    public static SyncError SyncFails(string masterRoot, StoreState state, ISyncUi? ui = null)
    {
        var slave = SlaveRegistry.List(state).Single();
        return SyncOrchestrator.Run(masterRoot, state, slave, ui ?? new ScriptedSyncUi(), nonInteractive: true)
            .ShouldBeOfType<Result<SyncRunOutput>.Fail>().Error;
    }

    public static LedgerHint Hint(StoreState state)
    {
        var payload = SlaveHints.Get(state, SlaveName).ShouldBeOfType<Result<byte[]>.Ok>().Value;
        var hint = LedgerHint.Parse(Encoding.UTF8.GetString(payload));
        hint.ShouldNotBeNull();
        return hint!;
    }

    public static IReadOnlyList<SyncHistoryRecord> History(StoreState state) =>
        SyncHistoryLog.List(state, SlaveName)
            .ShouldBeOfType<Result<IReadOnlyList<SyncHistoryRecord>>.Ok>().Value;
}
