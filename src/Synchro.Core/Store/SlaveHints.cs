namespace Synchro.Core.Store;

public static class SlaveHints
{
    public static Result<byte[]> Get(StoreState state, string slaveName)
    {
        var missing = SlaveLookup.RequireExisting(state, slaveName);
        if (missing is not null)
        {
            return new Result<byte[]>.Fail(missing);
        }

        return state.LedgerHints.TryGetValue(slaveName, out var payload)
            ? new Result<byte[]>.Ok(payload)
            : new Result<byte[]>.Fail(SyncError.NotFound($"no ledger hint stored for slave '{slaveName}'"));
    }

    public static Result<StoreState> Set(StoreState state, string slaveName, byte[] payload)
    {
        var missing = SlaveLookup.RequireExisting(state, slaveName);
        if (missing is not null)
        {
            return new Result<StoreState>.Fail(missing);
        }

        if (payload.Length == 0)
        {
            return new Result<StoreState>.Fail(SyncError.Invalid("ledger hint payload is empty"));
        }

        var hints = new Dictionary<string, byte[]>(state.LedgerHints, StringComparer.Ordinal)
        {
            [slaveName] = payload
        };
        return new Result<StoreState>.Ok(state with { LedgerHints = hints });
    }
}
