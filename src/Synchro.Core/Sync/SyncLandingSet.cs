using Synchro.Core.Landing;

namespace Synchro.Core.Sync;

public sealed record SyncLandingSet
{
    public required IReadOnlyList<LandingChange> ToSlave { get; init; }

    public required IReadOnlyList<LandingChange> ToMaster { get; init; }
}
