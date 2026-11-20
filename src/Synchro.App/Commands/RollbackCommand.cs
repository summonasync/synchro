using Synchro.Core;
using Synchro.Core.Git;
using Synchro.Core.Landing;
using Synchro.Core.Store;

namespace Synchro.App.Commands;

internal static class RollbackCommand
{
    public static int Run(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        if (!Directory.Exists(StorePaths.Folder(root)) || !StoreInitializer.TryLoadKey(root, out var key))
        {
            Console.Error.WriteLine("no store here — run 'synchro init' first");
            return 1;
        }

        var loaded = StoreContainer.Load(StorePaths.Container(root), key);
        if (loaded is not Result<StoreState>.Ok state)
        {
            Console.Error.WriteLine($"store unreadable: {((Result<StoreState>.Fail)loaded).Error.Message}");
            return 1;
        }

        var slaveName = args.FirstOrDefault(a => !a.StartsWith("--"));
        var resolved = ResolveSlave(state.Value, slaveName);
        if (resolved is not Result<SlaveConfig>.Ok slaveOk)
        {
            Console.Error.WriteLine($"rollback failed: {((Result<SlaveConfig>.Fail)resolved).Error.Message}");
            return slaveName is null ? 2 : 1;
        }

        var slave = slaveOk.Value;
        var receivingRoot = ResolveReceivingRoot(root, slave);
        if (receivingRoot is null)
        {
            Console.Error.WriteLine($"slave folder is missing: '{slave.Root}'");
            return 1;
        }

        var report = RollbackService.Rollback(receivingRoot, root, slave);
        if (report is not Result<RollbackReport>.Ok ok)
        {
            Console.Error.WriteLine($"rollback failed: {((Result<RollbackReport>.Fail)report).Error.Message}");
            return 1;
        }

        var value = ok.Value;
        if (!value.RolledBack)
        {
            Console.WriteLine($"nothing to roll back — '{value.Branch}' is checked out and clean");
            return 0;
        }

        Console.WriteLine(
            $"rollback: back on '{value.Branch}' — {value.RestoredPaths.Count} restored, {value.RemovedPaths.Count} removed");
        foreach (var path in value.RestoredPaths)
        {
            Console.WriteLine($"  restored: {path}");
        }

        foreach (var path in value.RemovedPaths)
        {
            Console.WriteLine($"  removed: {path}");
        }

        return 0;
    }

    private static Result<SlaveConfig> ResolveSlave(StoreState state, string? slaveName)
    {
        if (slaveName is not null)
        {
            return SlaveRegistry.Get(state, slaveName);
        }

        var slaves = SlaveRegistry.List(state);
        return slaves.Count switch
        {
            0 => new Result<SlaveConfig>.Fail(SyncError.NotFound(
                "no slaves registered — run 'synchro add slave' first")),
            1 => new Result<SlaveConfig>.Ok(slaves[0]),
            _ => new Result<SlaveConfig>.Fail(SyncError.Invalid(
                $"several slaves registered ({string.Join(", ", slaves.Select(s => s.Name))}) — pass one"))
        };
    }

    private static string? ResolveReceivingRoot(string masterRoot, SlaveConfig slave)
    {
        var masterBranch = ReceivingGit.CurrentBranch(masterRoot);
        if (masterBranch is Result<string>.Ok branch && branch.Value == slave.SyncBranch)
        {
            return masterRoot;
        }

        return Directory.Exists(slave.Root) ? slave.Root : null;
    }
}
