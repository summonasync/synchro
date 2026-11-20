namespace Synchro.Core.Install;

public sealed record LinkSwapped
{
    public required bool Created { get; init; }
    public required bool RePointed { get; init; }
    public required bool ForcedForeign { get; init; }
    public required string? ForeignTarget { get; init; }
}
