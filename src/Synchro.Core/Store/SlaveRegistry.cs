namespace Synchro.Core.Store;

public static class SlaveRegistry
{
    public static Result<StoreState> Add(StoreState state, SlaveConfig slave)
    {
        var invalid = Validate(slave.Name, slave.Root, slave.SyncBranch, slave.BaseBranch);
        if (invalid is not null)
        {
            return new Result<StoreState>.Fail(invalid);
        }

        if (state.Slaves.ContainsKey(slave.Name))
        {
            return new Result<StoreState>.Fail(SyncError.Conflict($"slave '{slave.Name}' is already registered"));
        }

        var slaves = new Dictionary<string, SlaveConfig>(state.Slaves, StringComparer.Ordinal)
        {
            [slave.Name] = slave
        };
        return new Result<StoreState>.Ok(state with { Slaves = slaves });
    }

    public static Result<SlaveConfig> Get(StoreState state, string name)
    {
        return state.Slaves.TryGetValue(name, out var slave)
            ? new Result<SlaveConfig>.Ok(slave)
            : new Result<SlaveConfig>.Fail(Missing(name));
    }

    public static IReadOnlyList<SlaveConfig> List(StoreState state) =>
        state.Slaves.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ToList();

    public static Result<StoreState> Update(
        StoreState state, string name, string root, string syncBranch, string baseBranch)
    {
        if (!state.Slaves.TryGetValue(name, out var slave))
        {
            return new Result<StoreState>.Fail(Missing(name));
        }

        var invalid = Validate(name, root, syncBranch, baseBranch);
        if (invalid is not null)
        {
            return new Result<StoreState>.Fail(invalid);
        }

        var slaves = new Dictionary<string, SlaveConfig>(state.Slaves, StringComparer.Ordinal)
        {
            [name] = slave with { Root = root, SyncBranch = syncBranch, BaseBranch = baseBranch }
        };
        return new Result<StoreState>.Ok(state with { Slaves = slaves });
    }

    public static Result<StoreState> Remove(StoreState state, string name)
    {
        if (!state.Slaves.ContainsKey(name))
        {
            return new Result<StoreState>.Fail(Missing(name));
        }

        var slaves = new Dictionary<string, SlaveConfig>(state.Slaves, StringComparer.Ordinal);
        slaves.Remove(name);
        var hints = new Dictionary<string, byte[]>(state.LedgerHints, StringComparer.Ordinal);
        hints.Remove(name);
        var history = new Dictionary<string, IReadOnlyList<SyncHistoryRecord>>(state.SyncHistory, StringComparer.Ordinal);
        history.Remove(name);

        return new Result<StoreState>.Ok(state with { Slaves = slaves, LedgerHints = hints, SyncHistory = history });
    }

    private static SyncError? Validate(string name, string root, string syncBranch, string baseBranch)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return SyncError.Invalid("slave name is required");
        }

        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            return SyncError.Invalid($"slave root must be an absolute path: '{root}'");
        }

        if (string.IsNullOrWhiteSpace(syncBranch))
        {
            return SyncError.Invalid("sync branch is required");
        }

        if (string.IsNullOrWhiteSpace(baseBranch))
        {
            return SyncError.Invalid("base branch is required");
        }

        return null;
    }

    private static SyncError Missing(string name) =>
        SyncError.NotFound($"no slave registered under '{name}'");
}
