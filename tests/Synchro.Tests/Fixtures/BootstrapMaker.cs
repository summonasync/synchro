namespace Synchro.Tests.Fixtures;

public static class BootstrapMaker
{
    public const string UserName = "synchro";
    public const string UserEmail = "synchro@localhost";
    public const string CommitMessage = "synchro bootstrap";
    public static readonly DateTimeOffset CommitDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static string Apply(string folderRoot)
    {
        FixtureGit.Output(folderRoot, "init", "-b", "main");
        FixtureGit.Output(folderRoot, "config", "user.name", UserName);
        FixtureGit.Output(folderRoot, "config", "user.email", UserEmail);
        FixtureGit.Output(folderRoot, "config", "commit.gpgsign", "false");

        var commit = FixtureGit.RunWithEnv(
            folderRoot,
            FixtureGit.DateEnv(CommitDate),
            "commit",
            "--allow-empty",
            "-m",
            CommitMessage);
        if (!commit.Succeeded)
            throw new InvalidOperationException(
                $"bootstrap commit failed in {folderRoot}: {commit.StandardError.Trim()}");

        return FixtureGit.Output(folderRoot, "rev-parse", "HEAD");
    }
}
