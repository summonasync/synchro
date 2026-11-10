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
        var args = baseSha is null
            ? (IReadOnlyList<string>)["log", "--pretty=format:%x1e%H%x00%s%x00", "--name-only", "--no-renames"]
            : (IReadOnlyList<string>)["log", "--pretty=format:%x1e%H%x00%s%x00", "--name-only", "--no-renames", $"{baseSha}..HEAD"];

        var result = GitRunner.Run(dir, [.. args]);
        if (!result.Ok)
        {
            return new Result<IReadOnlyList<CommitInfo>>.Fail(
                SyncError.Invalid($"git log failed: {result.StdErr.Trim()}"));
        }

        var commits = new List<CommitInfo>();
        foreach (var record in result.StdOut.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = record.Split('\n');
            var header = lines[0].Split('\0');
            if (header.Length < 2)
            {
                continue;
            }

            var files = lines.Skip(1)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            commits.Add(new CommitInfo
            {
                Sha = header[0],
                Message = header[1],
                Files = files
            });
        }

        return new Result<IReadOnlyList<CommitInfo>>.Ok(commits);
    }

    public static byte[] GetFileContent(string dir, string path) =>
        GitRunner.RunBytes(dir, "show", $"HEAD:{path}");

    public static IReadOnlyList<string> GetTrackedFiles(string dir) =>
        GitRunner.Run(dir, "ls-files", "-z")
            .StdOut
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
}
