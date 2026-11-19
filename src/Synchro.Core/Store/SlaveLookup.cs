namespace Synchro.Core.Store;

internal static class SlaveLookup
{
    public static SyncError? RequireExisting(StoreState state, string slaveName)
    {
        if (string.IsNullOrWhiteSpace(slaveName))
        {
            return SyncError.Invalid("slave name is required");
        }

        return state.Slaves.ContainsKey(slaveName)
            ? null
            : SyncError.NotFound($"no slave registered under '{slaveName}'");
    }
}
