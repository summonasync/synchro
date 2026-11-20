namespace Synchro.Tests.Fixtures;

public static class RebaseSquashCooker
{
    private static readonly DateTimeOffset SeedDate = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MasterDate = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SlaveFirstDate = new(2026, 3, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SlaveSecondDate = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SlaveSquashDate = new(2026, 3, 5, 9, 0, 0, TimeSpan.Zero);

    public sealed record Cooked(
        TempRepoBuilder Origin,
        TempRepoBuilder Master,
        TempRepoBuilder Slave,
        string SharedSeedSha,
        string MasterTipSha,
        string[] SlaveLocalShas,
        string SlaveSquashSha,
        string PreSquashTreeSha,
        string SquashTreeSha);

    public static Cooked Cook()
    {
        var origin = TempRepoBuilder.Create();
        var seedSha = origin.Commit("seed.txt", "shared seed content\n", "seed", SeedDate);

        var master = TempRepoBuilder.CloneFrom(origin.Root);
        var masterTipSha = master.Commit("master-note.txt", "master note\n", "master side commit", MasterDate);

        var slave = TempRepoBuilder.CloneFrom(origin.Root);
        var firstSha = slave.Commit("a.txt", "slave edit of a\n", "slave edit a", SlaveFirstDate);
        var secondSha = slave.Commit("b.txt", "slave adds b\n", "slave adds b", SlaveSecondDate);
        var preSquashTreeSha = TreeSha(slave.Root, "HEAD");

        FixtureGit.Output(slave.Root, "reset", "--soft", seedSha);
        var squash = FixtureGit.RunWithEnv(slave.Root, FixtureGit.DateEnv(SlaveSquashDate), "commit", "-m", "slave squashed work");
        if (!squash.Succeeded)
            throw new InvalidOperationException($"squash commit failed in {slave.Root}: {squash.StandardError.Trim()}");
        var squashSha = slave.HeadSha;
        var squashTreeSha = TreeSha(slave.Root, "HEAD");

        return new Cooked(
            origin,
            master,
            slave,
            seedSha,
            masterTipSha,
            [firstSha, secondSha],
            squashSha,
            preSquashTreeSha,
            squashTreeSha);
    }

    private static string TreeSha(string repoRoot, string revision) =>
        FixtureGit.Output(repoRoot, "rev-parse", $"{revision}^{{tree}}");
}
