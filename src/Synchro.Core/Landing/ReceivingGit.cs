using Synchro.Core;
using Synchro.Core.Git;

namespace Synchro.Core.Landing;

public static class ReceivingGit
{
    public static Result<string> CurrentBranch(string root)
    {
        var result = GitRunner.Run(root, "branch", "--show-current");
        if (!result.Ok)
        {
            return new Result<string>.Fail(SyncError.Invalid($"not a git repository: '{root}'"));
        }

        var name = result.StdOut.Trim();
        return name.Length == 0
            ? new Result<string>.Fail(SyncError.Invalid("detached HEAD — checkout a branch first"))
            : new Result<string>.Ok(name);
    }

    public static bool BranchExists(string root, string name) =>
        GitRunner.Run(root, "rev-parse", "--verify", "--quiet", $"refs/heads/{name}").Ok;

    public static Result<string> CreateBranch(string root, string name, string from)
    {
        var result = GitRunner.Run(root, "checkout", "-b", name, from);
        return result.Ok
            ? new Result<string>.Ok(name)
            : new Result<string>.Fail(SyncError.Conflict(
                $"cannot create branch '{name}' from '{from}': {result.StdErr.Trim()}"));
    }

    public static Result<string> Checkout(string root, string name)
    {
        var result = GitRunner.Run(root, "checkout", name);
        return result.Ok
            ? new Result<string>.Ok(name)
            : new Result<string>.Fail(SyncError.Conflict(
                $"cannot checkout '{name}': {result.StdErr.Trim()}"));
    }

    public static Result<WorktreeState> State(string root)
    {
        var result = GitRunner.Run(root, "status", "--porcelain", "-z");
        if (!result.Ok)
        {
            return new Result<WorktreeState>.Fail(
                SyncError.Invalid($"git status failed: {result.StdErr.Trim()}"));
        }

        var tracked = new List<string>();
        var untracked = new List<string>();
        var tokens = result.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Length <= 3)
            {
                continue;
            }

            var status = token[..2];
            var path = token[3..];
            if (status == "??")
            {
                untracked.Add(path);
                continue;
            }

            tracked.Add(path);
            if (status[0] is 'R' or 'C' && i + 1 < tokens.Length)
            {
                tracked.Add(tokens[i + 1]);
                i++;
            }
        }

        return new Result<WorktreeState>.Ok(new WorktreeState(tracked, untracked));
    }

    public static SyncError? DiscardAll(string root)
    {
        var reset = GitRunner.Run(root, "reset", "--quiet");
        if (!reset.Ok)
        {
            return SyncError.Invalid($"git reset failed: {reset.StdErr.Trim()}");
        }

        var restore = GitRunner.Run(root, "checkout", "--", ".");
        return restore.Ok
            ? null
            : SyncError.Invalid($"git checkout failed: {restore.StdErr.Trim()}");
    }

    public static void PruneEmptyParents(string root, string relativePath)
    {
        var separator = Path.DirectorySeparatorChar;
        var dir = Path.GetDirectoryName(Path.Combine(root, relativePath.Replace('/', separator)));
        var rootFull = Path.GetFullPath(root);
        while (dir is not null && dir.Length > rootFull.Length)
        {
            if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any())
            {
                return;
            }

            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }
}
