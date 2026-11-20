using Shouldly;
using Xunit;

namespace Synchro.Tests.Fixtures;

public class BootstrapMakerTests
{
    [Fact]
    public void Apply_OnPlainScaffold_ProducesNeutralSingleCommit()
    {
        var scaffold = PlainFolderScaffold.Create();
        try
        {
            BootstrapMaker.Apply(scaffold.Root);

            Directory.Exists(Path.Combine(scaffold.Root, ".git")).ShouldBeTrue();
            FixtureGit.Output(scaffold.Root, "symbolic-ref", "--short", "HEAD").ShouldBe("main");
            FixtureGit.Output(scaffold.Root, "rev-list", "--count", "HEAD").ShouldBe("1");
            FixtureGit.Output(scaffold.Root, "log", "-1", "--format=%an|%ae|%cn|%ce")
                .ShouldBe($"{BootstrapMaker.UserName}|{BootstrapMaker.UserEmail}|{BootstrapMaker.UserName}|{BootstrapMaker.UserEmail}");
            FixtureGit.Output(scaffold.Root, "log", "-1", "--format=%s").ShouldBe(BootstrapMaker.CommitMessage);
            FixtureGit.Output(scaffold.Root, "ls-tree", "-r", "HEAD").ShouldBe(string.Empty);

            foreach (var file in scaffold.Files)
                File.ReadAllText(Path.Combine(scaffold.Root, file.Path)).ShouldBe(file.Content);
            FixtureGit.Output(scaffold.Root, "status", "--porcelain")
                .ShouldContain("?? readme.md", Case.Insensitive);
        }
        finally
        {
            Directory.Delete(scaffold.Root, true);
        }
    }

    [Fact]
    public void Apply_Twice_ProducesIdenticalBootstrapSha()
    {
        var first = PlainFolderScaffold.Create();
        var second = PlainFolderScaffold.Create();
        try
        {
            var firstSha = BootstrapMaker.Apply(first.Root);
            var secondSha = BootstrapMaker.Apply(second.Root);

            firstSha.ShouldBe(secondSha);
        }
        finally
        {
            Directory.Delete(first.Root, true);
            Directory.Delete(second.Root, true);
        }
    }
}
