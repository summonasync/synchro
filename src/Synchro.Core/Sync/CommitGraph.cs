using Synchro.Core;
using Synchro.Core.Git;

namespace Synchro.Core.Sync;

public static class CommitGraph
{
    public static string? ResolveBranch(string repo, string branch)
    {
        var result = GitRunner.Run(repo, "rev-parse", "--verify", "--quiet", $"{branch}^{{commit}}");
        return result.Ok ? result.StdOut.Trim() : null;
    }

    public static Result<string> ObjectsDir(string repo)
    {
        var result = GitRunner.Run(repo, "rev-parse", "--git-path", "objects");
        if (!result.Ok)
        {
            return new Result<string>.Fail(
                SyncError.Invalid($"cannot locate the git objects directory: {result.StdErr.Trim()}"));
        }

        var raw = result.StdOut.Trim();
        var full = Path.IsPathFullyQualified(raw)
            ? raw
            : Path.GetFullPath(Path.Combine(repo, raw));
        return new Result<string>.Ok(full);
    }

    public static Result<string?> MergeBase(string repo, string? alternateObjectsDir, string a, string b)
    {
        var result = SyncGit.Run(repo, alternateObjectsDir, "merge-base", a, b);
        if (result.Ok)
        {
            return new Result<string?>.Ok(result.StdOut.Trim());
        }

        if (result.ExitCode == 1)
        {
            return new Result<string?>.Ok(null);
        }

        return new Result<string?>.Fail(SyncError.Invalid($"git merge-base failed: {result.StdErr.Trim()}"));
    }

    public static Result<IReadOnlyList<string>> Reachable(
        string repo, string? alternateObjectsDir, string head, string excludeHead)
    {
        var result = SyncGit.Run(repo, alternateObjectsDir, "rev-list", head, $"^{excludeHead}");
        if (!result.Ok)
        {
            return new Result<IReadOnlyList<string>>.Fail(
                SyncError.Invalid($"git rev-list failed: {result.StdErr.Trim()}"));
        }

        var shas = result.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Reverse()
            .ToArray();
        return new Result<IReadOnlyList<string>>.Ok(shas);
    }

    public static Result<IReadOnlyList<string>> FilesOf(string repo, string sha)
    {
        var shortSha = sha[..Math.Min(12, sha.Length)];
        var result = GitRunner.Run(
            repo, "diff-tree", "-r", "-z", "--name-status", "--no-commit-id", "--no-renames", "--root", sha);
        if (!result.Ok)
        {
            return new Result<IReadOnlyList<string>>.Fail(
                SyncError.Invalid($"git diff-tree failed for {shortSha}: {result.StdErr.Trim()}"));
        }

        var tokens = result.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var files = new List<string>();
        for (var i = 0; i + 1 < tokens.Length; i += 2)
        {
            files.Add(tokens[i + 1]);
        }

        return new Result<IReadOnlyList<string>>.Ok(files);
    }
}
