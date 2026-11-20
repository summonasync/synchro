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

            var masterPatchId = FixtureGit.StablePatchId(cooked.Master.Root, cooked.MasterCommitSha);
            var slavePatchId = FixtureGit.StablePatchId(cooked.Slave.Root, cooked.SlavePickSha);
            masterPatchId.ShouldNotBeEmpty();
            masterPatchId.ShouldBe(slavePatchId);

            File.ReadAllText(Path.Combine(cooked.Slave.Root, "feature.txt")).ShouldBe("feature work\n");
            File.ReadAllText(Path.Combine(cooked.Slave.Root, "slave-local.txt")).ShouldBe("slave own work\n");
        }
        finally
        {
            cooked.Origin.Delete();
            cooked.Master.Delete();
            cooked.Slave.Delete();
        }

        Directory.Exists(cooked.Origin.Root).ShouldBeFalse();
        Directory.Exists(cooked.Master.Root).ShouldBeFalse();
        Directory.Exists(cooked.Slave.Root).ShouldBeFalse();
    }
}
