using Synchro.Core;
using Synchro.Core.Git;
using Synchro.Core.Landing;
using Synchro.Core.Store;

namespace Synchro.App.Commands;

internal static class RollbackCommand
{
    public static int Run(string[] args)
    {
        var parsed = RollbackArgs.Parse(args);
        if (parsed is not Result<RollbackArgs>.Ok parsedOk)
        {
            Console.Error.WriteLine(((Result<RollbackArgs>.Fail)parsed).Error.Message);
            return 2;
        }

        if (parsedOk.Value.WantsHelp)
        {
            Console.WriteLine(RollbackArgs.Usage);
            return 0;
        }

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

        var slaveName = parsedOk.Value.SlaveName;
        var resolved = ResolveSlave(state.Value, slaveName);
        if (resolved is not Result<SlaveConfig>.Ok slaveOk)
        {
            Console.Error.WriteLine($"rollback failed: {((Result<SlaveConfig>.Fail)resolved).Error.Message}");
            return slaveName is null ? 2 : 1;
        }

        var receiver = RollbackReceiver.Resolve(root, slaveOk.Value, parsedOk.Value.PreferMaster);
        if (receiver is not Result<string>.Ok receiverOk)
        {
            Console.Error.WriteLine($"rollback failed: {((Result<string>.Fail)receiver).Error.Message}");
            return 1;
        }

        var report = RollbackService.Rollback(receiverOk.Value, root, slaveOk.Value);
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
            $"rollback: back on '{value.Branch}' — {value.RestoredPaths.Count} restored, {value.RemovedPaths.Count} removed"
            + (value.Skipped.Count > 0 ? $", {value.Skipped.Count} blocked" : string.Empty));
        foreach (var path in value.RestoredPaths)
        {
            Console.WriteLine($"  restored: {path}");
        }

        foreach (var path in value.RemovedPaths)
        {
            Console.WriteLine($"  removed: {path}");
        }

        foreach (var skipped in value.Skipped)
        {
            Console.WriteLine($"  blocked: {skipped.Path} — {skipped.Reason}");
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
}
