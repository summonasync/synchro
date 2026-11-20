namespace Synchro.Core.Landing;

public sealed record LandingEdits(
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Deleted)
{
    public static readonly LandingEdits Empty = new([], []);
}
