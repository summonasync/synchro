using Synchro.Core;
using Synchro.Core.Store;

namespace Synchro.App.Commands;

internal static class SlaveSelector
{
    public static Result<IReadOnlyList<SlaveConfig>> Select(StoreState state, string? name)
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
}
