namespace Synchro.Tests.Fixtures;

public static class UnrelatedHistoriesCooker
{
    private static readonly DateTimeOffset MasterDate = new(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SlaveDate = new(2026, 4, 2, 9, 0, 0, TimeSpan.Zero);

    public sealed record Cooked(
        TempRepoBuilder Master,
        TempRepoBuilder Slave,
        string MasterRootSha,
        string SlaveRootSha);

    public static Cooked Cook()
    {
        var master = TempRepoBuilder.Create();
        var masterRootSha = master.Commit("master.txt", "master root content\n", "master root", MasterDate);

        var slave = TempRepoBuilder.Create();
        var slaveRootSha = slave.Commit("slave.txt", "slave root content\n", "slave root", SlaveDate);

        return new Cooked(master, slave, masterRootSha, slaveRootSha);
    }
}
