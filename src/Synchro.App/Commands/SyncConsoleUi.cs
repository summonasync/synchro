using Synchro.Core.Store;
using Synchro.Core.Sync;

namespace Synchro.App.Commands;

internal sealed class SyncConsoleUi : ISyncUi
{
    public ConflictWinner ResolveConflict(string path)
    {
        while (true)
        {
            Console.Write($"conflict on '{path}' (changed on both sides) — [Enter]=master wins, s=slave wins: ");
            switch (Console.ReadLine()?.Trim().ToLowerInvariant())
            {
                case null or "":
                    return ConflictWinner.Master;
                case "s" or "slave":
                    return ConflictWinner.Slave;
                default:
                    Console.WriteLine("answer with Enter (master wins) or 's' (slave wins)");
                    break;
            }
        }
    }
}
