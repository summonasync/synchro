using Shouldly;
using Xunit;

namespace Synchro.Tests.Fixtures;

public class RebaseSquashCookerTests
{
    [Fact]
    public void Cook_Squash_RewritesHistoryKeepingContent()
    {
        var cooked = RebaseSquashCooker.Cook();
        try
        {
            cooked.SlaveSquashSha.ShouldNotBe(cooked.SlaveLocalShas[0]);
            cooked.SlaveSquashSha.ShouldNotBe(cooked.SlaveLocalShas[1]);
            cooked.SquashTreeSha.ShouldBe(cooked.PreSquashTreeSha);

            var squashPatchId = FixtureGit.StablePatchId(cooked.Slave.Root, cooked.SlaveSquashSha);
            squashPatchId.ShouldNotBe(FixtureGit.StablePatchId(cooked.Slave.Root, cooked.SlaveLocalShas[0]));
            squashPatchId.ShouldNotBe(FixtureGit.StablePatchId(cooked.Slave.Root, cooked.SlaveLocalShas[1]));

            FixtureGit.Run(cooked.Slave.Root, "merge-base", "--is-ancestor", cooked.SharedSeedSha, cooked.SlaveSquashSha)
                .Succeeded.ShouldBeTrue();
            FixtureGit.Output(cooked.Slave.Root, "rev-list", "--count", "HEAD").ShouldBe("2");

            File.ReadAllText(Path.Combine(cooked.Master.Root, "master-note.txt")).ShouldBe("master note\n");
            FixtureGit.Output(cooked.Master.Root, "rev-list", "--count", "HEAD").ShouldBe("2");
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
