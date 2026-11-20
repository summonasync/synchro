using Synchro.Core;
using Synchro.Core.Landing;
using Synchro.Core.Store;
using Synchro.Core.Sync;

namespace Synchro.App.Commands;

internal static class SyncCommand
{
    public static int Run(string[] args)
    {
        var parsed = SyncArgs.Parse(args);
        if (parsed is not Result<SyncArgs>.Ok parsedOk)
        {
            Console.Error.WriteLine(((Result<SyncArgs>.Fail)parsed).Error.Message);
            return 2;
        }

        if (parsedOk.Value.WantsHelp)
        {
            Console.WriteLine(SyncArgs.Usage);
            return 0;
        }

        var root = Directory.GetCurrentDirectory();
        var store = MasterStore.Load(root);
        if (store is not Result<(StoreState State, byte[] Key)>.Ok storeOk)
        {
            Console.Error.WriteLine(((Result<(StoreState, byte[])>.Fail)store).Error.Message);
            return 1;
        }

        var resolved = SyncSlaveResolver.Resolve(storeOk.Value.State, parsedOk.Value.SlaveName);
        if (resolved is not Result<SlaveConfig>.Ok slaveOk)
        {
            Console.Error.WriteLine($"sync failed: {((Result<SlaveConfig>.Fail)resolved).Error.Message}");
            return parsedOk.Value.SlaveName is null ? 2 : 1;
        }

        var run = SyncOrchestrator.Run(
            root, storeOk.Value.State, slaveOk.Value, new SyncConsoleUi(), parsedOk.Value.AssumeYes);
        if (run is not Result<SyncRunOutput>.Ok runOk)
        {
            Console.Error.WriteLine($"sync failed: {((Result<SyncRunOutput>.Fail)run).Error.Message}");
            return 1;
        }

        StoreContainer.Save(StorePaths.Container(root), storeOk.Value.Key, runOk.Value.State);
        WriteReport(runOk.Value.Report);
        return 0;
    }

    private static void WriteReport(SyncRunReport report)
    {
        if (report.NonInteractive && report.Conflicts.Count > 0)
        {
            Console.WriteLine("non-interactive run — master wins applied to every conflict");
        }

        foreach (var conflict in report.Conflicts)
        {
            Console.WriteLine(
                $"  conflict '{conflict.Path}': {(conflict.Winner == ConflictWinner.Master ? "master wins" : "slave wins")}");
        }

        foreach (var path in report.AlreadyCommitted)
        {
            Console.WriteLine($"  '{path}' is already committed in the master inbox — skipped");
        }

        WriteLanding("slave", report.ToSlave);
        WriteLanding("master", report.ToMaster);

        if (report.ToSlave is null && report.ToMaster is null)
        {
            Console.WriteLine($"sync '{report.SlaveName}': nothing to land — both sides already carry the synced content");
            return;
        }

        if (report.ToSlave is { } toSlave)
        {
            Console.WriteLine(
                $"next: the slave repo is parked on '{toSlave.Branch}' — commit the inbox to keep it,"
                + $" or run 'synchro rollback {report.SlaveName}' to discard");
        }

        if (report.ToMaster is { } toMaster)
        {
            Console.WriteLine(
                $"next: the master repo is parked on '{toMaster.Branch}' — commit the inbox to keep it,"
                + $" or run 'synchro rollback {report.SlaveName} --master' to discard");
        }
    }

    private static void WriteLanding(string side, LandingReport? landing)
    {
        if (landing is null)
        {
            return;
        }

        Console.WriteLine(
            $"landed on the {side}: branch '{landing.Branch}' ({(landing.BranchCreated ? "created" : "reused")}),"
            + $" {landing.WrittenFiles.Count} written, {landing.DeletedFiles.Count} deleted");
        foreach (var path in landing.WrittenFiles)
        {
            Console.WriteLine($"  wrote: {path}");
        }

        foreach (var path in landing.DeletedFiles)
        {
            Console.WriteLine($"  deleted: {path}");
        }
    }
}
