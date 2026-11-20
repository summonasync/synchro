namespace Synchro.Tests.Fixtures;

public sealed class TempRepoBuilder
{
    public const string UserName = "fixture";
    public const string UserEmail = "fixture@localhost";

    public string Root { get; }

    private TempRepoBuilder(string root) => Root = root;

    public static TempRepoBuilder Create()
    {
        var root = TempDir.Create();
        FixtureGit.Output(root, "init", "-b", "main");
        PinIdentity(root);
        return new TempRepoBuilder(root);
    }

    public static TempRepoBuilder CloneFrom(string sourceRoot)
    {
        var root = TempDir.Create();
        FixtureGit.Output(root, "clone", sourceRoot, ".");
        PinIdentity(root);
        return new TempRepoBuilder(root);
    }

    public string HeadSha => FixtureGit.Output(Root, "rev-parse", "HEAD");

    public string Commit(string path, string content, string message, DateTimeOffset date) =>
        CommitFiles([new RepoFile(path, content)], message, date);

    public string CommitFiles(IReadOnlyList<RepoFile> files, string message, DateTimeOffset date)
    {
        foreach (var file in files)
        {
            var fullPath = System.IO.Path.Combine(Root, file.Path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, file.Content);
            FixtureGit.Output(Root, "add", file.Path);
        }

        var result = FixtureGit.RunWithEnv(Root, FixtureGit.DateEnv(date), "commit", "-m", message);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"commit '{message}' failed in {Root}: {result.StandardError.Trim()}");
        return HeadSha;
    }

    public void Branch(string name, string? startPoint = null)
    {
        if (startPoint is null)
            FixtureGit.Output(Root, "branch", name);
        else
            FixtureGit.Output(Root, "branch", name, startPoint);
    }

    public void Tag(string name, string? startPoint = null)
    {
        if (startPoint is null)
            FixtureGit.Output(Root, "tag", name);
        else
            FixtureGit.Output(Root, "tag", name, startPoint);
    }

    public void Checkout(string name) => FixtureGit.Output(Root, "checkout", name);

    public void Delete() => Directory.Delete(Root, true);

    private static void PinIdentity(string root)
    {
        FixtureGit.Output(root, "config", "user.name", UserName);
        FixtureGit.Output(root, "config", "user.email", UserEmail);
        FixtureGit.Output(root, "config", "commit.gpgsign", "false");
        FixtureGit.Output(root, "config", "core.autocrlf", "false");
    }
}
