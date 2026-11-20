using Synchro.Core;
using Synchro.Core.Store;

namespace Synchro.App.Commands;

internal static class MasterStore
{
    public static Result<(StoreState State, byte[] Key)> Load(string root)
    {
        if (!Directory.Exists(StorePaths.Folder(root)) || !StoreInitializer.TryLoadKey(root, out var key))
        {
            return new Result<(StoreState, byte[])>.Fail(
                SyncError.NotFound("no store here — run 'synchro init' first"));
        }

        var loaded = StoreContainer.Load(StorePaths.Container(root), key);
        return loaded is Result<StoreState>.Ok state
            ? new Result<(StoreState, byte[])>.Ok((state.Value, key))
            : new Result<(StoreState, byte[])>.Fail(((Result<StoreState>.Fail)loaded).Error);
    }
}
