namespace Synchro.Tests.Fixtures;

public static class PlainFolderScaffold
{
    public sealed record Scaffold(string Root, IReadOnlyList<RepoFile> Files);

    public static Scaffold Create()
    {
        var root = TempDir.Create();
        RepoFile[] files =
        [
            new RepoFile("readme.md", "# plain folder\n"),
            new RepoFile("notes/todo.txt", "- draft task\n")
        ];
        foreach (var file in files)
        {
            var fullPath = Path.Combine(root, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, file.Content);
        }

        return new Scaffold(root, files);
    }
}
