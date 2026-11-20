using Synchro.Core;
using Synchro.Core.Store;
using Synchro.Core.Sync;

namespace Synchro.App.Commands;

internal static class StatusCommand
{
    public const string Usage =
        "usage: synchro status [<slave>] — per-slave config, ledger hint, inbox state, last operation";

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

        var selected = SlaveSelector.Select(storeOk.Value.State, parsedOk.Value.SlaveName);
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
            WriteSlave(SyncStatusReader.Read(root, storeOk.Value.State, slave));
        }

        return 0;
    }

    private static void WriteSlave(SlaveStatus status)
    {
        var config = status.Config;
        Console.WriteLine($"slave '{config.Name}' — {config.Root}");
        Console.WriteLine($"  sync branch '{config.SyncBranch}', base branch '{config.BaseBranch}'");
        Console.WriteLine($"  ledger hint: {SyncStatusFormatter.Hint(status.Hint)}");
        Console.WriteLine($"  slave inbox: {SyncStatusFormatter.Inbox(config.SyncBranch, status.SlaveInbox)}");
        Console.WriteLine($"  master inbox: {SyncStatusFormatter.Inbox(config.SyncBranch, status.MasterInbox)}");
        Console.WriteLine(
            status.InFlight
                ? "  in flight: yes — a previous sync crashed mid-run; sort it out, then delete the marker"
                : "  in flight: no");
        Console.WriteLine($"  last op: {SyncStatusFormatter.Operation(status.LastOperation)}");
    }
}
