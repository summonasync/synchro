namespace Synchro.Tests.Fixtures;

public static class BothSidesNewCooker
{
    private static readonly DateTimeOffset SeedDate = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MasterDate = new(2026, 1, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SlaveDate = new(2026, 1, 3, 9, 0, 0, TimeSpan.Zero);

    public sealed record Cooked(
        TempRepoBuilder Origin,
        TempRepoBuilder Master,
        TempRepoBuilder Slave,
        string SharedSeedSha,
        string MasterTipSha,
        string SlaveTipSha);

    public static Cooked Cook()
    {
        var origin = TempRepoBuilder.Create();
        var seedSha = origin.Commit("seed.txt", "shared seed content\n", "seed", SeedDate);

        var master = TempRepoBuilder.CloneFrom(origin.Root);
        var slave = TempRepoBuilder.CloneFrom(origin.Root);

        var masterTipSha = master.Commit("master-only.txt", "master side work\n", "master side commit", MasterDate);
        var slaveTipSha = slave.Commit("slave-only.txt", "slave side work\n", "slave side commit", SlaveDate);

        return new Cooked(origin, master, slave, seedSha, masterTipSha, slaveTipSha);
    }
}
