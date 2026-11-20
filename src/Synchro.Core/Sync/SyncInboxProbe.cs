using Synchro.Core.Git;
using Synchro.Core.Hashing;

namespace Synchro.Core.Sync;

public static class SyncInboxProbe
{
    public static IReadOnlySet<string> CommittedPaths(
        string masterRoot, string? masterSyncTip, IReadOnlyList<FileDelta> deltas)
    {
        if (masterSyncTip is null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var committed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var delta in deltas)
        {
            if (delta.Kind != FileDeltaKind.SlaveToMaster)
            {
                continue;
            }

            var inbox = GitChangeService.GetFileContent(masterRoot, delta.Path, masterSyncTip);
            if (inbox is Result<byte[]>.Ok content)
            {
                if (delta.SlaveHash is not null && ContentHash.Compute(content.Value) == delta.SlaveHash)
                {
                    committed.Add(delta.Path);
                }
            }
            else if (delta.SlaveHash is null)
            {
                committed.Add(delta.Path);
            }
        }

        return committed;
    }
}
