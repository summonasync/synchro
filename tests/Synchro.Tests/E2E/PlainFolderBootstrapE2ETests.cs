using Shouldly;
using Synchro.Core.Slave;
using Synchro.Tests.Fixtures;
using Xunit;

namespace Synchro.Tests.E2E;

public class PlainFolderBootstrapE2ETests
{
    private static readonly DateTimeOffset MasterDate = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FolderDate = new(2026, 5, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AcceptDate = new(2026, 5, 3, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PlainFolder_ConsentBootstrapsNeutralCommit_AndSyncsLikeAnyGitRepo()
    {
        var master = TempRepoBuilder.Create();
        var scaffold = PlainFolderScaffold.Create();
        try
        {
            master.Commit("master.txt", "master work\n", "master seed", MasterDate);

            var registered = AddSlaveService.Run(
                    master.Root,
                    ScenarioPair.EmptyState(),
                    scaffold.Root,
                    new ScriptedAddSlaveUi(name: "plain-slave"))
                .ShouldBeOfType<AddSlaveResult.Registered>();

            registered.RepoBootstrapped.ShouldBeTrue();
            registered.Config.Root.ShouldBe(scaffold.Root);
            registered.Config.BaseBranch.ShouldBe("main");
            registered.Config.SyncBranch.ShouldBe("feature-sync");
            AuditBootstrap(scaffold.Root);

            FolderCommitFiles(
                scaffold.Root,
                [
                    new RepoFile("readme.md", "# plain folder\n"),
                    new RepoFile("notes/todo.txt", "- draft task\n"),
                    new RepoFile("folder-work.txt", "folder human work\n")
                ],
                "folder commits its work",
                FolderDate);

            var first = ScenarioPair.Sync(master.Root, registered.State);

            first.Report.Conflicts.ShouldBeEmpty();
            first.Report.ToSlave!.WrittenFiles.ShouldBe(["master.txt"]);
            first.Report.ToMaster!.WrittenFiles.ShouldBe(["folder-work.txt", "notes/todo.txt", "readme.md"]);
            File.ReadAllText(Path.Combine(scaffold.Root, "master.txt")).ShouldBe("master work\n");
            File.ReadAllText(Path.Combine(master.Root, "folder-work.txt")).ShouldBe("folder human work\n");

            FixtureGit.Output(scaffold.Root, "checkout", "main");
            FolderCommitFiles(
                scaffold.Root,
                [new RepoFile("master.txt", "master work\n")],
                "absorb master content",
                AcceptDate);
            master.Checkout("main");
            master.CommitFiles(
                [
                    new RepoFile("folder-work.txt", "folder human work\n"),
                    new RepoFile("notes/todo.txt", "- draft task\n"),
                    new RepoFile("readme.md", "# plain folder\n")
                ],
                "absorb slave content",
                AcceptDate);

            var second = ScenarioPair.Sync(master.Root, first.State);

            second.Report.ToSlave.ShouldBeNull();
            second.Report.ToMaster.ShouldBeNull();
            second.Report.Conflicts.ShouldBeEmpty();
            second.Report.AlreadyCommitted.ShouldBeEmpty();
            GitCli.Run(scaffold.Root, "branch", "--show-current").Output.Trim().ShouldBe("main");
            GitCli.Run(master.Root, "branch", "--show-current").Output.Trim().ShouldBe("main");
        }
        finally
        {
            master.Delete();
            Directory.Delete(scaffold.Root, true);
        }
    }

    [Fact]
    public void PlainFolder_ConsentDeclined_FolderStaysUntouched()
    {
        var master = TempRepoBuilder.Create();
        var scaffold = PlainFolderScaffold.Create();
        try
        {
            AddSlaveService.Run(
                    master.Root,
                    ScenarioPair.EmptyState(),
                    scaffold.Root,
                    new ScriptedAddSlaveUi(name: "shy-slave", consent: false))
                .ShouldBeOfType<AddSlaveResult.ConsentDeclined>();

            Directory.Exists(Path.Combine(scaffold.Root, ".git")).ShouldBeFalse();
            scaffold.Files.Select(f => File.ReadAllText(Path.Combine(scaffold.Root, f.Path)))
                .ShouldBe(scaffold.Files.Select(f => f.Content).ToList());
        }
        finally
        {
            master.Delete();
            Directory.Delete(scaffold.Root, true);
        }
    }

    private static void AuditBootstrap(string folderRoot)
    {
        GitCli.Run(folderRoot, "rev-list", "--count", "HEAD").Output.Trim().ShouldBe("1");
        GitCli.Run(folderRoot, "config", "user.name").Output.Trim().ShouldBe(BootstrapMaker.UserName);
        var log = GitCli.Run(folderRoot, "log", "-1", "--pretty=%an%n%ae%n%s").Output.Trim();
        log.ShouldBe($"{BootstrapMaker.UserName}\n{BootstrapMaker.UserEmail}\n{BootstrapMaker.CommitMessage}");

        var commitObject = GitCli.Run(folderRoot, "cat-file", "commit", "HEAD").Output;
        commitObject.ShouldNotContain(Environment.MachineName);
        commitObject.ShouldNotContain(Environment.UserName);
        commitObject.ShouldNotContain(folderRoot);
    }

    private static void FolderCommitFiles(
        string root,
        IReadOnlyList<RepoFile> files,
        string message,
        DateTimeOffset date)
    {
        foreach (var file in files)
        {
            var fullPath = Path.Combine(root, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, file.Content);
            FixtureGit.Output(root, "add", file.Path);
        }

        FixtureGit.RunWithEnv(root, FixtureGit.DateEnv(date), "commit", "-m", message).Succeeded.ShouldBeTrue();
    }
}
