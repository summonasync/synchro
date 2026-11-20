using System.Text;
using Shouldly;
using Synchro.Core.Hashing;
using Synchro.Core.Sync;
using Xunit;

namespace Synchro.Tests;

public class SyncInboxProbeTests
{
    [Fact]
    public void CommittedPaths_SlaveToMasterContentMatchesTip_IsSuppressed()
    {
        var repo = CreateRepo();
        try
        {
            TestGitRepo.Commit(repo, "s.txt", "S\n", "accept inbox");
            var tip = TipOf(repo);

            var committed = SyncInboxProbe.CommittedPaths(
                repo, tip, [Delta(FileDeltaKind.SlaveToMaster, "s.txt", Hash("S\n"))]);

            committed.ShouldBe(["s.txt"]);
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void CommittedPaths_SlaveToMasterContentMismatch_IsNotSuppressed()
    {
        var repo = CreateRepo();
        try
        {
            TestGitRepo.Commit(repo, "s.txt", "S\n", "accept inbox");
            var tip = TipOf(repo);

            var committed = SyncInboxProbe.CommittedPaths(
                repo, tip, [Delta(FileDeltaKind.SlaveToMaster, "s.txt", Hash("S2\n"))]);

            committed.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void CommittedPaths_SlaveToMasterDeletionMatchesTip_IsSuppressed()
    {
        var repo = CreateRepo();
        try
        {
            var committed = SyncInboxProbe.CommittedPaths(
                repo, TipOf(repo), [Delta(FileDeltaKind.SlaveToMaster, "gone.txt", null)]);

            committed.ShouldBe(["gone.txt"]);
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void CommittedPaths_ConflictContentMatchesTip_IsNotSuppressed()
    {
        var repo = CreateRepo();
        try
        {
            TestGitRepo.Commit(repo, "s.txt", "S\n", "accept inbox");
            var tip = TipOf(repo);

            var committed = SyncInboxProbe.CommittedPaths(
                repo,
                tip,
                [Delta(FileDeltaKind.Conflict, "s.txt", Hash("S\n"), Hash("S2\n"))]);

            committed.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void CommittedPaths_ConflictDeletion_IsNotSuppressed()
    {
        var repo = CreateRepo();
        try
        {
            var committed = SyncInboxProbe.CommittedPaths(
                repo, TipOf(repo), [Delta(FileDeltaKind.Conflict, "gone.txt", null, Hash("S\n"))]);

            committed.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void CommittedPaths_MasterToSlaveDelta_IsNeverProbed()
    {
        var repo = CreateRepo();
        try
        {
            TestGitRepo.Commit(repo, "s.txt", "S\n", "accept inbox");
            var tip = TipOf(repo);

            var committed = SyncInboxProbe.CommittedPaths(
                repo,
                tip,
                [Delta(FileDeltaKind.MasterToSlave, "s.txt", Hash("S\n"), Hash("S2\n"))]);

            committed.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void CommittedPaths_NullTip_SuppressesNothing()
    {
        var committed = SyncInboxProbe.CommittedPaths(
            "unused", null, [Delta(FileDeltaKind.SlaveToMaster, "s.txt", Hash("S\n"))]);

        committed.ShouldBeEmpty();
    }

    private static string CreateRepo()
    {
        var repo = TestGitRepo.Create();
        TestGitRepo.Commit(repo, "seed.txt", "seed\n", "seed");
        GitCli.Run(repo, "checkout", "-b", "feature-sync");
        return repo;
    }

    private static string TipOf(string repo) => GitCli.Run(repo, "rev-parse", "HEAD").Output.Trim();

    private static string Hash(string content) => ContentHash.Compute(Encoding.UTF8.GetBytes(content));

    private static FileDelta Delta(FileDeltaKind kind, string path, string? slaveHash, string? masterHash = null) =>
        new() { Path = path, Kind = kind, SlaveHash = slaveHash, MasterHash = masterHash };
}
