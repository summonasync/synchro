using Synchro.Core.Git;

namespace Synchro.Core.Slave;

public static class SlavePathGuard
{
    public static Result<string> Resolve(string masterRoot, string rawPath)
    {
        string candidate;
        try
        {
            candidate = CanonicalPath.Resolve(rawPath, masterRoot);
        }
        catch (ArgumentException)
        {
            return new Result<string>.Fail(SyncError.Invalid($"slave path is not usable: '{rawPath}'"));
        }

        if (!Directory.Exists(candidate))
        {
            return new Result<string>.Fail(
                SyncError.NotFound($"slave path does not exist or is not a directory: '{rawPath}'"));
        }

        var master = CanonicalPath.Resolve(masterRoot, masterRoot);
        if (SamePath(candidate, master))
        {
            return new Result<string>.Fail(
                SyncError.Conflict($"'{rawPath}' is the master repo itself — a slave must be a separate folder"));
        }

        if (candidate.StartsWith(master + Path.DirectorySeparatorChar, PathComparison))
        {
            return new Result<string>.Fail(
                SyncError.Conflict($"'{rawPath}' is nested inside the master working tree — slaves must live outside it"));
        }

        if (GitRepoLayout.IsLinkedGitDir(candidate))
        {
            return new Result<string>.Fail(
                SyncError.Conflict($"'{rawPath}' is a git worktree linked to another repo — register the checkout that owns its own .git"));
        }

        return new Result<string>.Ok(candidate);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(left, right, PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
