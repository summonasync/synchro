using Shouldly;
using Xunit;

namespace Synchro.Tests.Fixtures;

public class CherryPickCookerTests
{
    [Fact]
    public void Cook_PickedChange_MatchesOriginalPatchIdWithDifferentSha()
    {
        var cooked = CherryPickCooker.Cook();
        try
        {
            cooked.MasterCommitSha.ShouldNotBe(cooked.SlavePickSha);
            cooked.SlavePickSha.ShouldNotBe(cooked.SlaveLocalSha);
            cooked.SharedSeedSha.ShouldNotBe(cooked.MasterCommitSha);

            var masterPatchId = StablePatchId(cooked.Master.Root, cooked.MasterCommitSha);
            var slavePatchId = StablePatchId(cooked.Slave.Root, cooked.SlavePickSha);
            masterPatchId.ShouldNotBeEmpty();
            masterPatchId.ShouldBe(slavePatchId);

            File.ReadAllText(Path.Combine(cooked.Slave.Root, "feature.txt")).ShouldBe("feature work\n");
            File.ReadAllText(Path.Combine(cooked.Slave.Root, "slave-local.txt")).ShouldBe("slave own work\n");
        }
        finally
        {
            cooked.Master.Delete();
            cooked.Slave.Delete();
        }
    }

    private static string StablePatchId(string repoRoot, string sha)
    {
        var patch = FixtureGit.Output(repoRoot, "show", sha);
        var result = FixtureGit.RunWithInput(repoRoot, patch, "patch-id", "--stable");
        result.Succeeded.ShouldBeTrue(result.StandardError);
        return result.StandardOutput.Trim().Split(' ')[0];
    }
}
