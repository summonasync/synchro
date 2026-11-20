using Shouldly;
using Xunit;

namespace Synchro.Tests.Fixtures;

public class BothSidesNewCookerTests
{
    [Fact]
    public void Cook_SidesDiverge_FromSharedSeed()
    {
        var cooked = BothSidesNewCooker.Cook();
        try
        {
            cooked.MasterTipSha.ShouldNotBe(cooked.SharedSeedSha);
            cooked.SlaveTipSha.ShouldNotBe(cooked.SharedSeedSha);
            cooked.MasterTipSha.ShouldNotBe(cooked.SlaveTipSha);

            foreach (var side in new[] { cooked.Master, cooked.Slave })
            {
                FixtureGit.Output(side.Root, "cat-file", "-e", cooked.SharedSeedSha);
                SeedIsAncestorOf(side.Root, cooked.SharedSeedSha, side.HeadSha).ShouldBeTrue();
            }

            File.ReadAllText(Path.Combine(cooked.Master.Root, "master-only.txt")).ShouldBe("master side work\n");
            File.ReadAllText(Path.Combine(cooked.Slave.Root, "slave-only.txt")).ShouldBe("slave side work\n");
        }
        finally
        {
            cooked.Origin.Delete();
            cooked.Master.Delete();
            cooked.Slave.Delete();
        }
    }

    [Fact]
    public void Cook_Twice_YieldsIdenticalStates()
    {
        var first = BothSidesNewCooker.Cook();
        var second = BothSidesNewCooker.Cook();
        try
        {
            first.SharedSeedSha.ShouldBe(second.SharedSeedSha);
            first.MasterTipSha.ShouldBe(second.MasterTipSha);
            first.SlaveTipSha.ShouldBe(second.SlaveTipSha);
        }
        finally
        {
            first.Origin.Delete();
            first.Master.Delete();
            first.Slave.Delete();
            second.Origin.Delete();
            second.Master.Delete();
            second.Slave.Delete();
        }
    }

    private static bool SeedIsAncestorOf(string repoRoot, string seedSha, string tipSha)
    {
        var result = FixtureGit.Run(repoRoot, "merge-base", "--is-ancestor", seedSha, tipSha);
        return result.Succeeded;
    }
}
