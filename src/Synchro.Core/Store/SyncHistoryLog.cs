namespace Synchro.Core.Store;

public static class SyncHistoryLog
{
    public static Result<StoreState> Append(StoreState state, string slaveName, SyncHistoryRecord record)
    {
        var missing = SlaveLookup.RequireExisting(state, slaveName);
        if (missing is not null)
        {
            return new Result<StoreState>.Fail(missing);
        }

        if (record.FilesChanged < 0)
        {
            return new Result<StoreState>.Fail(SyncError.Invalid("files-changed count cannot be negative"));
        }

        state.SyncHistory.TryGetValue(slaveName, out var existing);
        var history = new Dictionary<string, IReadOnlyList<SyncHistoryRecord>>(
            state.SyncHistory, StringComparer.Ordinal)
        {
            [slaveName] = [record, .. existing ?? []]
        };

        return new Result<StoreState>.Ok(state with { SyncHistory = history });
    }

    public static Result<IReadOnlyList<SyncHistoryRecord>> List(StoreState state, string slaveName)
    {
        var missing = SlaveLookup.RequireExisting(state, slaveName);
        if (missing is not null)
        {
            return new Result<IReadOnlyList<SyncHistoryRecord>>.Fail(missing);
        }

        return state.SyncHistory.TryGetValue(slaveName, out var history)
            ? new Result<IReadOnlyList<SyncHistoryRecord>>.Ok(history)
            : new Result<IReadOnlyList<SyncHistoryRecord>>.Ok([]);
    }
}
