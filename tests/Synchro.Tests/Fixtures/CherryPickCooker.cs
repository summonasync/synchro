namespace Synchro.Tests.Fixtures;

public static class CherryPickCooker
{
    private static readonly DateTimeOffset SeedDate = new(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MasterDate = new(2026, 2, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SlaveLocalDate = new(2026, 2, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PickCommitterDate = new(2026, 2, 4, 9, 0, 0, TimeSpan.Zero);

    public sealed record Cooked(
        TempRepoBuilder Origin,
        TempRepoBuilder Master,
        TempRepoBuilder Slave,
        string SharedSeedSha,
        string MasterCommitSha,
        string SlaveLocalSha,
        string SlavePickSha);

    public static Cooked Cook()
    {
        var origin = TempRepoBuilder.Create();
        var seedSha = origin.Commit("seed.txt", "shared seed content\n", "seed", SeedDate);

        var master = TempRepoBuilder.CloneFrom(origin.Root);
        var masterCommitSha = master.Commit("feature.txt", "feature work\n", "add feature", MasterDate);

        var slave = TempRepoBuilder.CloneFrom(origin.Root);
        const string pickRef = "refs/temp/013-cherry-pick";
        FixtureGit.Output(slave.Root, "fetch", master.Root, $"main:{pickRef}");
        var slaveLocalSha = slave.Commit("slave-local.txt", "slave own work\n", "slave local work", SlaveLocalDate);

        var pick = FixtureGit.RunWithEnv(slave.Root, FixtureGit.DateEnv(PickCommitterDate), "cherry-pick", pickRef);
        if (!pick.Succeeded)
            throw new InvalidOperationException($"cherry-pick failed in {slave.Root}: {pick.StandardError.Trim()}");
        FixtureGit.Output(slave.Root, "update-ref", "-d", pickRef);
        var slavePickSha = slave.HeadSha;

        return new Cooked(origin, master, slave, seedSha, masterCommitSha, slaveLocalSha, slavePickSha);
    }
}
