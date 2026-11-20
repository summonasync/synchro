using Synchro.Core;
using Synchro.Core.Git;

namespace Synchro.Core.Sync;

public static class PatchId
{
    public static Result<string?> For(string repo, string sha)
    {
        var shortSha = sha[..Math.Min(12, sha.Length)];
        var diff = GitRunner.Run(repo, "diff-tree", "-p", "--root", "--no-commit-id", "--no-renames", sha);
        if (!diff.Ok)
        {
            return new Result<string?>.Fail(
                SyncError.Invalid($"git diff-tree failed for {shortSha}: {diff.StdErr.Trim()}"));
        }

        var stable = SyncGit.RunWithStdin(repo, null, diff.StdOut, "patch-id", "--stable");
        if (!stable.Ok)
        {
            return new Result<string?>.Fail(
                SyncError.Invalid($"git patch-id failed for {shortSha}: {stable.StdErr.Trim()}"));
        }

        var line = stable.StdOut.Trim();
        var id = line.Length == 0
            ? null
            : line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return new Result<string?>.Ok(id);
    }
}
