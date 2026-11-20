namespace Synchro.Core.Landing;

public sealed record LandingReport
{
    public required bool BranchCreated { get; init; }
    public required string Branch { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
    public required IReadOnlyList<string> DeletedFiles { get; init; }
}
