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

            var enriched = WithTreeBlobs(slaveRoot, commitOk.Value);
            if (enriched is not Result<SyncCommit>.Ok enrichedOk)
            {
                return new Result<IReadOnlyList<SyncCommit>>.Fail(((Result<SyncCommit>.Fail)enriched).Error);
            }

            commits.Add(enrichedOk.Value);
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

    public static OfferDisposition Classify(SyncCommit offer, IReadOnlyList<SyncCommit> syncCommits, string offerRoot)
    {
        if (offer.PatchId is not null && syncCommits.Any(s => s.PatchId == offer.PatchId))
        {
            return OfferDisposition.AppliedCommitted;
        }

        if (offer.Files.Count == 0 || syncCommits.Count == 0)
        {
            return OfferDisposition.Reoffer;
        }

        var offerPost = TreeBlobs(offerRoot, offer.Files, offer.Sha);
        if (offerPost is not Result<IReadOnlyDictionary<string, string>>.Ok postOk)
        {
            return OfferDisposition.Reoffer;
        }

        var offerPre = ParentTreeBlobs(offerRoot, offer.Files, offer.Sha);

        var accounted = 0;
        var modified = false;
        foreach (var file in offer.Files)
        {
            var correspondence = Correspondence(
                syncCommits,
                file,
                postOk.Value.GetValueOrDefault(file),
                offerPre.GetValueOrDefault(file));
            if (correspondence == FileCorrespondence.Unaccounted)
            {
                continue;
            }

            accounted++;
            modified |= correspondence == FileCorrespondence.Modified;
        }

        if (accounted < offer.Files.Count)
        {
            return OfferDisposition.Reoffer;
        }

        return modified ? OfferDisposition.AppliedModified : OfferDisposition.AppliedCommitted;
    }

    private static FileCorrespondence Correspondence(
        IReadOnlyList<SyncCommit> syncCommits, string file, string? offerPostBlob, string? offerPreBlob)
    {
        var touched = false;
        var reached = false;
        var movedPast = false;
        foreach (var commit in syncCommits)
        {
            if (!commit.Files.Contains(file))
            {
                continue;
            }

            var pre = commit.PreBlobs.GetValueOrDefault(file);
            var post = commit.PostBlobs.GetValueOrDefault(file);
            if (!touched && pre == offerPreBlob && post != offerPostBlob)
            {
                return FileCorrespondence.Modified;
            }

            touched = true;
            reached |= post == offerPostBlob || pre == offerPostBlob;
            movedPast |= pre == offerPostBlob && post != offerPostBlob;
        }

        if (!touched || !reached)
        {
            return FileCorrespondence.Unaccounted;
        }

        return movedPast ? FileCorrespondence.Modified : FileCorrespondence.Committed;
    }

    private static Result<SyncCommit> WithTreeBlobs(string repo, SyncCommit commit)
    {
        if (commit.Files.Count == 0)
        {
            return new Result<SyncCommit>.Ok(commit);
        }

        var post = TreeBlobs(repo, commit.Files, commit.Sha);
        if (post is not Result<IReadOnlyDictionary<string, string>>.Ok postOk)
        {
            return new Result<SyncCommit>.Fail(((Result<IReadOnlyDictionary<string, string>>.Fail)post).Error);
        }

        return new Result<SyncCommit>.Ok(commit with
        {
            PostBlobs = postOk.Value,
            PreBlobs = ParentTreeBlobs(repo, commit.Files, commit.Sha)
        });
    }

    private static Result<IReadOnlyDictionary<string, string>> TreeBlobs(
        string repo, IReadOnlyList<string> files, string rev)
    {
        if (files.Count == 0)
        {
            return new Result<IReadOnlyDictionary<string, string>>.Ok(SyncCommit.NoBlobs);
        }

        string[] args = ["ls-tree", "-r", "-z", rev, "--", .. files];
        var result = SyncGit.Run(repo, null, args);
        if (!result.Ok)
        {
            return new Result<IReadOnlyDictionary<string, string>>.Fail(
                SyncError.Invalid($"git ls-tree failed for {ShortSha(rev)}: {result.StdErr.Trim()}"));
        }

        return new Result<IReadOnlyDictionary<string, string>>.Ok(ParseBlobs(result.StdOut));
    }

    private static IReadOnlyDictionary<string, string> ParentTreeBlobs(
        string repo, IReadOnlyList<string> files, string sha)
    {
        var parent = TreeBlobs(repo, files, $"{sha}^");
        return parent is Result<IReadOnlyDictionary<string, string>>.Ok parentOk
            ? parentOk.Value
            : SyncCommit.NoBlobs;
    }

    private static IReadOnlyDictionary<string, string> ParseBlobs(string output)
    {
        var blobs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t');
            if (tab < 0)
            {
                continue;
            }

            var meta = entry[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (meta.Length == 3 && meta[1] == "blob")
            {
                blobs[entry[(tab + 1)..]] = meta[2];
            }
        }

        return blobs;
    }

    private static string ShortSha(string rev) => rev[..Math.Min(12, rev.Length)];

    private enum FileCorrespondence
    {
        Unaccounted,
        Committed,
        Modified
    }
}
