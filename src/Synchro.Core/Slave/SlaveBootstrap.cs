using Synchro.Core.Git;

namespace Synchro.Core.Slave;

public static class SlaveBootstrap
{
    public const string IdentityName = "synchro";
    public const string IdentityEmail = "synchro@localhost";
    public const string CommitMessage = "synchro bootstrap";

    public static SyncError? Init(string root, string baseBranch)
    {
        var init = GitRunner.Run(root, "init", "-b", baseBranch);
        if (!init.Ok)
        {
            return SyncError.Invalid($"git init failed: {init.StdErr.Trim()}");
        }

        GitRunner.Run(root, "config", "user.name", IdentityName);
        GitRunner.Run(root, "config", "user.email", IdentityEmail);

        var commit = GitRunner.Run(root, "commit", "--allow-empty", "-m", CommitMessage);
        return commit.Ok ? null : SyncError.Invalid($"bootstrap commit failed: {commit.StdErr.Trim()}");
    }
}
