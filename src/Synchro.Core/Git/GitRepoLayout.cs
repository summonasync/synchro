using Synchro.Core;

namespace Synchro.Core.Git;

public static class GitRepoLayout
{
    public static bool IsWorkTree(string root) => Directory.Exists(Path.Combine(root, ".git"));

    public static bool IsLinkedGitDir(string root)
    {
        var gitPath = Path.Combine(root, ".git");
        if (!File.Exists(gitPath))
        {
            return false;
        }

        try
        {
            var firstLine = File.ReadLines(gitPath).FirstOrDefault();
            return firstLine is null || firstLine.TrimStart().StartsWith("gitdir:", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return true;
        }
    }

    public static string? CurrentBranch(string root)
    {
        if (!IsWorkTree(root))
        {
            return null;
        }

        var branch = GitRunner.Run(root, "branch", "--show-current").StdOut.Trim();
        return branch.Length > 0 ? branch : null;
    }
}
