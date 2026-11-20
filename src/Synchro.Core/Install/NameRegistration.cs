namespace Synchro.Core.Install;

public sealed record NameRegistration
{
    public required bool Registered { get; init; }
    public required bool PathReady { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}
