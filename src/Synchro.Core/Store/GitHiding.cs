namespace Synchro.Core.Store;

public static class GitHiding
{
    public static readonly string[] SynchroLines = ["/.synchro/", "*.synchr"];

    public static IReadOnlyList<string> EnsureExcluded(string root)
    {
        var excludePath = Path.Combine(root, ".git", "info", "exclude");
        var existing = File.Exists(excludePath) ? File.ReadAllLines(excludePath) : [];
        var missing = SynchroLines.Where(line => !existing.Contains(line)).ToArray();

        if (missing.Length == 0)
        {
            return [];
        }

        File.AppendAllLines(excludePath, missing);
        return missing;
    }
}
