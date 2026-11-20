using Shouldly;
using Xunit;

namespace Synchro.Tests.Fixtures;

public class PlainFolderScaffoldTests
{
    [Fact]
    public void Create_WritesKnownFiles_WithoutGit()
    {
        var scaffold = PlainFolderScaffold.Create();
        try
        {
            Directory.Exists(Path.Combine(scaffold.Root, ".git")).ShouldBeFalse();

            foreach (var file in scaffold.Files)
                File.ReadAllText(Path.Combine(scaffold.Root, file.Path)).ShouldBe(file.Content);

            scaffold.Files.Select(f => f.Path).ShouldBe(["readme.md", "notes/todo.txt"]);
        }
        finally
        {
            Directory.Delete(scaffold.Root, true);
        }
    }
}
