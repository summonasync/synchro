using Synchro.Core.Store;
using Synchro.Core.Sync;

namespace Synchro.Tests;

public sealed class ScriptedSyncUi(IReadOnlyDictionary<string, ConflictWinner>? winners = null) : ISyncUi
{
    public ConflictWinner ResolveConflict(string path) =>
        winners is not null && winners.TryGetValue(path, out var winner)
            ? winner
            : ConflictWinner.Master;
}
