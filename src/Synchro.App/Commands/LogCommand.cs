using Synchro.Core;
using Synchro.Core.Store;
using Synchro.Core.Sync;

namespace Synchro.App.Commands;

internal static class LogCommand
{
    public const string Usage = "usage: synchro log [<slave>] — sync history, newest first";

    public static int Run(string[] args)
    {
        var parsed = SlaveNameArgs.Parse(args, Usage);
        if (parsed is not Result<SlaveNameArgs>.Ok parsedOk)
        {
            Console.Error.WriteLine(((Result<SlaveNameArgs>.Fail)parsed).Error.Message);
            return 2;
        }

        if (parsedOk.Value.WantsHelp)
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var root = Directory.GetCurrentDirectory();
        var store = MasterStore.Load(root);
        if (store is not Result<(StoreState State, byte[] Key)>.Ok storeOk)
        {
            Console.Error.WriteLine(((Result<(StoreState, byte[])>.Fail)store).Error.Message);
            return 1;
        }

        var selected = SelectSlaves(storeOk.Value.State, parsedOk.Value.SlaveName);
        if (selected is not Result<IReadOnlyList<SlaveConfig>>.Ok slavesOk)
        {
            Console.Error.WriteLine(((Result<IReadOnlyList<SlaveConfig>>.Fail)selected).Error.Message);
            return 1;
        }

        if (slavesOk.Value.Count == 0)
        {
            Console.WriteLine("no slaves registered — run 'synchro add slave <path>' first");
            return 0;
        }

        foreach (var slave in slavesOk.Value)
        {
            WriteHistory(storeOk.Value.State, slave.Name);
        }

        return 0;
    }

    private static Result<IReadOnlyList<SlaveConfig>> SelectSlaves(StoreState state, string? name)
    {
        if (name is null)
        {
            return new Result<IReadOnlyList<SlaveConfig>>.Ok(SlaveRegistry.List(state));
        }

        var single = SlaveRegistry.Get(state, name);
        return single is Result<SlaveConfig>.Ok slave
            ? new Result<IReadOnlyList<SlaveConfig>>.Ok([slave.Value])
            : new Result<IReadOnlyList<SlaveConfig>>.Fail(((Result<SlaveConfig>.Fail)single).Error);
    }

    private static void WriteHistory(StoreState state, string slaveName)
    {
        Console.WriteLine($"history for '{slaveName}' (newest first):");
        var history = SyncHistoryLog.List(state, slaveName);
        var records = history is Result<IReadOnlyList<SyncHistoryRecord>>.Ok list ? list.Value : [];
        if (records.Count == 0)
        {
            Console.WriteLine("  no syncs yet");
            return;
        }

        foreach (var record in records)
        {
            Console.WriteLine($"  {SyncStatusFormatter.Operation(record)}");
        }
    }
}
