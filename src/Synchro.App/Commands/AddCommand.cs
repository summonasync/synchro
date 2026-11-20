using Synchro.Core;
using Synchro.Core.Slave;
using Synchro.Core.Store;

namespace Synchro.App.Commands;

internal static class AddCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] != "slave")
        {
            Console.Error.WriteLine("usage: synchro add slave <path>");
            return 2;
        }

        if (args.Length > 2)
        {
            Console.Error.WriteLine($"unexpected argument '{args[2]}'");
            Console.Error.WriteLine("usage: synchro add slave <path>");
            return 2;
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

        var outcome = AddSlaveService.Run(root, state.Value, args[1], new AddSlaveConsoleUi());

        if (outcome is AddSlaveResult.Registered registered)
        {
            StoreContainer.Save(StorePaths.Container(root), key, registered.State);
            WriteSummary(registered);
            return 0;
        }

        if (outcome is AddSlaveResult.ConsentDeclined)
        {
            Console.WriteLine("consent declined — the folder was left untouched");
            return 0;
        }

        Console.Error.WriteLine($"add slave failed: {((AddSlaveResult.Refused)outcome).Error.Message}");
        return 1;
    }

    private static void WriteSummary(AddSlaveResult.Registered done)
    {
        var config = done.Config;
        Console.WriteLine($"slave registered: {config.Name}");
        Console.WriteLine($"  path: {config.Root}");
        Console.WriteLine($"  sync branch: {config.SyncBranch}");
        Console.WriteLine($"  base branch: {config.BaseBranch}");
        if (done.RepoBootstrapped)
        {
            Console.WriteLine("  repo: plain folder bootstrapped (one neutral commit, recorded in sync history)");
        }
    }
}
