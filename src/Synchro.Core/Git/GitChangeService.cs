using Synchro.Core;

namespace Synchro.Core.Git;

public sealed record GitChanges(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Modified,
    IReadOnlyList<string> Deleted);

public static class GitChangeService
{
    public static Result<string> GetHeadSha(string dir)
    {
        var result = GitRunner.Run(dir, "rev-parse", "HEAD");
        return result.Ok
            ? new Result<string>.Ok(result.StdOut.Trim())
            : new Result<string>.Fail(SyncError.Invalid("no commits yet — commit your work first"));
    }

    public static bool IsDirty(string dir) =>
        GitRunner.Run(dir, "status", "--porcelain").StdOut.Trim().Length > 0;

    public static Result<GitChanges> GetChanges(string dir, string baseSha)
    {
        var result = GitRunner.Run(dir, "diff", "--name-status", "-z", "--no-renames", baseSha, "HEAD");
        if (!result.Ok)
        {
            return new Result<GitChanges>.Fail(SyncError.Invalid($"git diff failed: {result.StdErr.Trim()}"));
        }

        var added = new List<string>();
        var modified = new List<string>();
        var deleted = new List<string>();

        var tokens = result.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < tokens.Length; i += 2)
        {
            var status = tokens[i];
            var path = tokens[i + 1];
            switch (status)
            {
                case "A":
                    added.Add(path);
                    break;
                case "D":
                    deleted.Add(path);
                    break;
                default:
                    modified.Add(path);
                    break;
            }
        }

        return new Result<GitChanges>.Ok(new GitChanges(added, modified, deleted));
    }

    public static Result<IReadOnlyList<CommitInfo>> GetCommitLog(string dir, string? baseSha)
    {
        var range = baseSha is null ? "HEAD" : $"{baseSha}..HEAD";
        var result = GitRunner.Run(
            dir,
            "log",
            "-z",
            "--pretty=format:%x1e%H%x00%s%x00",
            "--name-only",
            "--no-renames",
            range);
        if (!result.Ok)
        {
            return new Result<IReadOnlyList<CommitInfo>>.Fail(
                SyncError.Invalid($"git log failed: {result.StdErr.Trim()}"));
        }

        var commits = new List<CommitInfo>();
        foreach (var record in result.StdOut.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var tokens = record.Split('\0');
            if (tokens.Length < 2)
            {
                continue;
            }

            commits.Add(new CommitInfo
            {
                Sha = tokens[0],
                Message = tokens[1],
                Files = [.. tokens.Skip(2).Select(t => t.TrimStart('\n')).Where(t => t.Length > 0)]
            });
        }

        return new Result<IReadOnlyList<CommitInfo>>.Ok(commits);
    }

    public static Result<byte[]> GetFileContent(string dir, string path)
    {
        try
        {
            return new Result<byte[]>.Ok(GitRunner.RunBytes(dir, "show", $"HEAD:{path}"));
        }
        catch (GitCommandException ex)
        {
            return new Result<byte[]>.Fail(SyncError.Invalid($"cannot read '{path}' from HEAD: {ex.Message}"));
        }
    }

    public static IReadOnlyList<string> GetHeadTreeFiles(string dir) =>
        GitRunner.Run(dir, "ls-tree", "-r", "-z", "--name-only", "HEAD")
            .StdOut
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
}
