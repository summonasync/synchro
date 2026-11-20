using Shouldly;
using Xunit;

namespace Synchro.Tests.Fixtures;

public class UnrelatedHistoriesCookerTests
{
    [Fact]
    public void Cook_Roots_AreFullyIndependent()
    {
        var cooked = UnrelatedHistoriesCooker.Cook();
        try
        {
            cooked.MasterRootSha.ShouldNotBe(cooked.SlaveRootSha);
            FixtureGit.Output(cooked.Master.Root, "rev-list", "--count", "HEAD").ShouldBe("1");
            FixtureGit.Output(cooked.Slave.Root, "rev-list", "--count", "HEAD").ShouldBe("1");

            FixtureGit.Run(cooked.Master.Root, "cat-file", "-e", cooked.SlaveRootSha)
                .Succeeded.ShouldBeFalse();
            FixtureGit.Run(cooked.Slave.Root, "cat-file", "-e", cooked.MasterRootSha)
                .Succeeded.ShouldBeFalse();

            File.ReadAllText(Path.Combine(cooked.Master.Root, "master.txt")).ShouldBe("master root content\n");
            File.ReadAllText(Path.Combine(cooked.Slave.Root, "slave.txt")).ShouldBe("slave root content\n");
        }
        finally
        {
            cooked.Master.Delete();
            cooked.Slave.Delete();
        }
    }
}
