using System.Security.Cryptography;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Store;
using Synchro.Core.Sync;

namespace Synchro.Tests;

public static class TestSyncPair
{
    public static (string Master, string Slave, byte[] Key, StoreState State) Create(string seedFileName = "seed")
    {
        var root = TempDir.Create();
        var seed = Path.Combine(root, "seed");
        Directory.CreateDirectory(seed);
        GitCli.Init(seed);
        GitCli.Run(seed, "config", "user.name", "tester");
        GitCli.Run(seed, "config", "user.email", "tester@test.local");
        File.WriteAllText(Path.Combine(seed, seedFileName), "seed\n");
        GitCli.Run(seed, "add", ".");
        GitCli.Run(seed, "commit", "-m", "seed");

        var master = Path.Combine(root, "master");
        var slave = Path.Combine(root, "slave");
        GitCli.Run(seed, "clone", "--quiet", seed, master);
        GitCli.Run(seed, "clone", "--quiet", seed, slave);
        foreach (var clone in new[] { master, slave })
        {
            GitCli.Run(clone, "config", "user.name", "tester");
            GitCli.Run(clone, "config", "user.email", "tester@test.local");
        }

        var (key, state) = InitStoreWithSlave(master, slave);
        return (master, slave, key, state);
    }

    public static (byte[] Key, StoreState State) InitStoreWithSlave(
        string master, string slaveRoot, string slaveName = "laptop")
    {
        var key = RandomNumberGenerator.GetBytes(32);
        StoreInitializer.Initialize(master, new InitOptions { KeyHex = Convert.ToHexStringLower(key) });
        var state = StoreContainer.Load(StorePaths.Container(master), key)
            .ShouldBeOfType<Result<StoreState>.Ok>().Value;
        state = SlaveRegistry.Add(state, new SlaveConfig
        {
            Name = slaveName,
            Root = slaveRoot,
            SyncBranch = "feature-sync",
            BaseBranch = "main",
            CreatedAtUtc = DateTimeOffset.UtcNow
        }).ShouldBeOfType<Result<StoreState>.Ok>().Value;

        return (key, state);
    }

    public static SyncRunOutput Sync(string master, StoreState state, ISyncUi? ui = null, bool nonInteractive = true)
    {
        var slave = SlaveRegistry.List(state).Single();
        var run = SyncOrchestrator.Run(master, state, slave, ui ?? new ScriptedSyncUi(), nonInteractive);
        return run.ShouldBeOfType<Result<SyncRunOutput>.Ok>().Value;
    }

    public static SyncError SyncFails(string master, StoreState state, ISyncUi? ui = null, bool nonInteractive = true)
    {
        var slave = SlaveRegistry.List(state).Single();
        var run = SyncOrchestrator.Run(master, state, slave, ui ?? new ScriptedSyncUi(), nonInteractive);
        return run.ShouldBeOfType<Result<SyncRunOutput>.Fail>().Error;
    }

    public static void Cleanup(string master)
    {
        Directory.Delete(Directory.GetParent(master)!.FullName, true);
    }
}
