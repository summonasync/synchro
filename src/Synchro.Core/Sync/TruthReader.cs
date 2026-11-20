using Synchro.Core;

namespace Synchro.Core.Sync;

public static class TruthReader
{
    public static Result<IReadOnlyList<SyncCommit>> Read(string slaveRoot, string syncBranch, string baseBranch)
    {
        var syncHead = CommitGraph.ResolveBranch(slaveRoot, syncBranch);
        if (syncHead is null)
        {
            return new Result<IReadOnlyList<SyncCommit>>.Ok([]);
        }

        var shas = CommitGraph.Reachable(slaveRoot, null, syncHead, baseBranch);
        if (shas is not Result<IReadOnlyList<string>>.Ok shasOk)
        {
            return new Result<IReadOnlyList<SyncCommit>>.Fail(((Result<IReadOnlyList<string>>.Fail)shas).Error);
        }

        var commits = new List<SyncCommit>();
        foreach (var sha in shasOk.Value)
        {
            var commit = Load(slaveRoot, sha);
            if (commit is not Result<SyncCommit>.Ok commitOk)
            {
                return new Result<IReadOnlyList<SyncCommit>>.Fail(((Result<SyncCommit>.Fail)commit).Error);
            }

            commits.Add(commitOk.Value);
        }

        return new Result<IReadOnlyList<SyncCommit>>.Ok(commits);
    }

    public static Result<SyncCommit> Load(string repo, string sha)
    {
        var patchId = PatchId.For(repo, sha);
        if (patchId is not Result<string?>.Ok patchOk)
        {
            return new Result<SyncCommit>.Fail(((Result<string?>.Fail)patchId).Error);
        }

        var files = CommitGraph.FilesOf(repo, sha);
        if (files is not Result<IReadOnlyList<string>>.Ok filesOk)
        {
            return new Result<SyncCommit>.Fail(((Result<IReadOnlyList<string>>.Fail)files).Error);
        }

        return new Result<SyncCommit>.Ok(new SyncCommit
        {
            Sha = sha,
            PatchId = patchOk.Value,
            Files = filesOk.Value
        });
    }

    public static OfferDisposition Classify(SyncCommit offer, IReadOnlyList<SyncCommit> syncCommits)
    {
        if (offer.PatchId is not null && syncCommits.Any(s => s.PatchId == offer.PatchId))
        {
            return OfferDisposition.AppliedCommitted;
        }

        var touched = offer.Files.ToHashSet(StringComparer.Ordinal);
        if (touched.Count > 0 && syncCommits.Any(s => s.Files.Any(touched.Contains)))
        {
            return OfferDisposition.AppliedModified;
        }

        return OfferDisposition.Reoffer;
    }
}
