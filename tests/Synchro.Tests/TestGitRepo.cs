namespace Synchro.Tests;

public static class TestGitRepo
{
    public static string Create()
    {
        var dir = TempDir.Create();
        GitCli.Run(dir, "init", "-b", "main");
        GitCli.Run(dir, "config", "user.name", "tester");
        GitCli.Run(dir, "config", "user.email", "tester@test.local");
        return dir;
    }

    public static string Commit(string dir, string path, string content, string message)
    {
        var fullPath = Path.Combine(dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        GitCli.Run(dir, "add", path);
        GitCli.Run(dir, "commit", "-m", message);
        return GitCli.Run(dir, "rev-parse", "HEAD").Output.Trim();
    }

    public static string DeleteAndCommit(string dir, string path, string message)
    {
        File.Delete(Path.Combine(dir, path));
        GitCli.Run(dir, "add", "-A");
        GitCli.Run(dir, "commit", "-m", message);
        return GitCli.Run(dir, "rev-parse", "HEAD").Output.Trim();
    }
}
