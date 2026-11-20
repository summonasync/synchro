namespace Synchro.Core.Landing;

public sealed record WorktreeState(
    IReadOnlyList<string> TrackedDirty,
    IReadOnlyList<string> Untracked)
{
    public bool IsClean => TrackedDirty.Count == 0 && Untracked.Count == 0;
}
