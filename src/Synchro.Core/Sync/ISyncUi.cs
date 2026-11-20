using Synchro.Core.Store;

namespace Synchro.Core.Sync;

public interface ISyncUi
{
    ConflictWinner ResolveConflict(string path);
}
