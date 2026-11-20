using Synchro.Core.Git;
using Synchro.Core.Store;

namespace Synchro.Core.Slave;

public static class AddSlaveService
{
    public const string DefaultSyncBranch = "feature-sync";
    public const string PlainFolderDefaultBaseBranch = "main";

    public static AddSlaveResult Run(string masterRoot, StoreState state, string rawPath, IAddSlaveUi ui)
    {
        var resolved = SlavePathGuard.Resolve(masterRoot, rawPath);
        if (resolved is not Result<string>.Ok path)
        {
            return new AddSlaveResult.Refused(((Result<string>.Fail)resolved).Error);
        }

        var root = path.Value;
        var name = ui.AskSlaveName().Trim();
        if (name.Length == 0)
        {
            return new AddSlaveResult.Refused(SyncError.Invalid("slave name is required"));
        }

        if (state.Slaves.ContainsKey(name))
        {
            return new AddSlaveResult.Refused(SyncError.Conflict($"slave '{name}' is already registered"));
        }

        var hasGitWorkTree = GitRepoLayout.IsWorkTree(root);
        var detectedBase = hasGitWorkTree ? GitRepoLayout.CurrentBranch(root) : PlainFolderDefaultBaseBranch;

        var syncBranch = Defaulted(ui.AskSyncBranch(DefaultSyncBranch)) ?? DefaultSyncBranch;
        var baseBranch = Defaulted(ui.AskBaseBranch(detectedBase)) ?? detectedBase;
        if (baseBranch is null)
        {
            return new AddSlaveResult.Refused(
                SyncError.Invalid("base branch is required — the repo is on a detached HEAD"));
        }

        var bootstrapped = false;
        if (!hasGitWorkTree)
        {
            if (!ui.ConfirmBootstrap(root))
            {
                return new AddSlaveResult.ConsentDeclined();
            }

            if (SlaveBootstrap.Init(root, baseBranch) is { } bootstrapError)
            {
                return new AddSlaveResult.Refused(bootstrapError);
            }

            bootstrapped = true;
        }

        var config = new SlaveConfig
        {
            Name = name,
            Root = root,
            SyncBranch = syncBranch,
            BaseBranch = baseBranch,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var added = SlaveRegistry.Add(state, config);
        if (added is not Result<StoreState>.Ok registered)
        {
            return new AddSlaveResult.Refused(((Result<StoreState>.Fail)added).Error);
        }

        var finalState = registered.Value;
        if (bootstrapped)
        {
            var appended = SyncHistoryLog.Append(finalState, name, BootstrapRecord());
            if (appended is not Result<StoreState>.Ok withHistory)
            {
                return new AddSlaveResult.Refused(((Result<StoreState>.Fail)appended).Error);
            }

            finalState = withHistory.Value;
        }

        return new AddSlaveResult.Registered(finalState, config, bootstrapped);
    }

    private static string? Defaulted(string entered) =>
        string.IsNullOrWhiteSpace(entered) ? null : entered.Trim();

    private static SyncHistoryRecord BootstrapRecord() => new()
    {
        AtUtc = DateTimeOffset.UtcNow,
        Direction = SyncDirection.MasterToSlave,
        FilesChanged = 0,
        Conflicts = [],
        RolledBack = false
    };
}
