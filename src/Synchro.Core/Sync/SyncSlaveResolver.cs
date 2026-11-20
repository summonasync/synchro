using Synchro.Core;
using Synchro.Core.Store;

namespace Synchro.Core.Sync;

public static class SyncSlaveResolver
{
    public static Result<SlaveConfig> Resolve(StoreState state, string? name)
    {
        if (name is not null)
        {
            return SlaveRegistry.Get(state, name);
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
