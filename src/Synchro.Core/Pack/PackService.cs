using Synchro.Core.Git;
using Synchro.Core.Hashing;
using Synchro.Core.Store;

namespace Synchro.Core.Pack;

public sealed record PackOptions
{
    public bool Full { get; init; }
}

public sealed record PackDraft
{
    public required SyncManifest Manifest { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public StoreState? UpdatedState { get; init; }
}

public static class PackService
{
    public static Result<PackDraft> Pack(string root, StoreState state, PackOptions options) =>
        state.Channel.Mode == SyncMode.Git
            ? PackGit(root, state, options)
            : PackPlain(root, state);

    private static Result<PackDraft> PackGit(string root, StoreState state, PackOptions options)
    {
        var head = GitChangeService.GetHeadSha(root);
        if (head is not Result<string>.Ok headOk)
        {
            return new Result<PackDraft>.Fail(((Result<string>.Fail)head).Error);
        }

        string? baseSha;
        if (options.Full)
        {
            baseSha = null;
        }
        else if (state.PeerKnownLocalPosition is { } known)
        {
            var ancestor = GitRunner.Run(root, "merge-base", "--is-ancestor", known, "HEAD");
            if (ancestor.ExitCode == 1)
            {
                return new Result<PackDraft>.Fail(SyncError.Conflict(
                    $"peer-known position {known[..Math.Min(12, known.Length)]} is not in this history — was a package applied out of order?"));
            }

            if (ancestor.ExitCode != 0)
            {
                return new Result<PackDraft>.Fail(SyncError.Invalid(
                    $"git merge-base failed: {ancestor.StdErr.Trim()}"));
            }

            baseSha = known;
        }
        else
        {
            return new Result<PackDraft>.Fail(SyncError.Invalid(
                "no peer-known position yet — the first package must be a baseline: use 'synchro pack --full'"));
        }

        Result<GitChanges> changes = baseSha is null
            ? new Result<GitChanges>.Ok(new GitChanges(
                GitChangeService.GetHeadTreeFiles(root),
                [],
                []))
            : GitChangeService.GetChanges(root, baseSha);
        if (changes is not Result<GitChanges>.Ok changesOk)
        {
            return new Result<PackDraft>.Fail(((Result<GitChanges>.Fail)changes).Error);
        }

        var log = GitChangeService.GetCommitLog(root, baseSha);
        if (log is not Result<IReadOnlyList<CommitInfo>>.Ok logOk)
        {
            return new Result<PackDraft>.Fail(((Result<IReadOnlyList<CommitInfo>>.Fail)log).Error);
        }

        var warnings = new List<string>();
        if (GitChangeService.IsDirty(root))
        {
            warnings.Add("working tree has uncommitted changes — packing committed state only");
        }

        var files = CollectFiles(
            changesOk.Value.Added.Concat(changesOk.Value.Modified),
            path => GitChangeService.GetFileContent(root, path));
        if (files is not Result<List<PackageFileEntry>>.Ok filesOk)
        {
            return new Result<PackDraft>.Fail(((Result<List<PackageFileEntry>>.Fail)files).Error);
        }

        var manifest = new SyncManifest
        {
            SchemaVersion = 1,
            Channel = state.Channel.Name,
            Mode = SyncMode.Git,
            BasePosition = baseSha is null ? null : new GitPosition(baseSha),
            NewPosition = new GitPosition(headOk.Value),
            Features = [],
            Commits = logOk.Value,
            Files = filesOk.Value,
            Deletions = [.. changesOk.Value.Deleted]
        };

        return new Result<PackDraft>.Ok(new PackDraft { Manifest = manifest, Warnings = warnings });
    }

    private static Result<PackDraft> PackPlain(string root, StoreState state)
    {
        var current = PlainTree.ComputeHashes(root);
        var sent = state.SentFileHashes;

        var changed = current
            .Where(kvp => !sent.TryGetValue(kvp.Key, out var hash) || hash != kvp.Value)
            .Select(kvp => kvp.Key)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        var deleted = sent.Keys
            .Except(current.Keys)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        if (changed.Count == 0 && deleted.Count == 0)
        {
            return new Result<PackDraft>.Fail(SyncError.Invalid(
                "nothing changed since the last package — the tree matches the sent state"));
        }

        var seq = state.SentChainSeq + 1;
        var rootHash = PlainTree.ComputeRootHash(current);

        var files = CollectFiles(changed, path =>
        {
            try
            {
                return new Result<byte[]>.Ok(PlainTree.ReadFile(root, path));
            }
            catch (IOException ex)
            {
                return new Result<byte[]>.Fail(SyncError.Invalid($"cannot read '{path}': {ex.Message}"));
            }
        });
        if (files is not Result<List<PackageFileEntry>>.Ok filesOk)
        {
            return new Result<PackDraft>.Fail(((Result<List<PackageFileEntry>>.Fail)files).Error);
        }

        var manifest = new SyncManifest
        {
            SchemaVersion = 1,
            Channel = state.Channel.Name,
            Mode = SyncMode.Plain,
            BasePosition = state.SentChainSeq > 0 ? new ChainPosition(state.SentChainSeq, state.SentChainRootHash ?? "") : null,
            NewPosition = new ChainPosition(seq, rootHash),
            Features = [],
            Commits = [],
            Files = filesOk.Value,
            Deletions = deleted
        };

        var updatedState = state with
        {
            SentFileHashes = current,
            SentChainSeq = seq,
            SentChainRootHash = rootHash
        };

        return new Result<PackDraft>.Ok(new PackDraft
        {
            Manifest = manifest,
            Warnings = [],
            UpdatedState = updatedState
        });
    }

    private static Result<List<PackageFileEntry>> CollectFiles(
        IEnumerable<string> paths,
        Func<string, Result<byte[]>> readContent)
    {
        var entries = new List<PackageFileEntry>();
        foreach (var path in paths.OrderBy(p => p, StringComparer.Ordinal))
        {
            var content = readContent(path);
            if (content is not Result<byte[]>.Ok contentOk)
            {
                return new Result<List<PackageFileEntry>>.Fail(((Result<byte[]>.Fail)content).Error);
            }

            entries.Add(new PackageFileEntry
            {
                Path = path,
                Hash = ContentHash.Compute(contentOk.Value),
                Content = contentOk.Value
            });
        }

        return new Result<List<PackageFileEntry>>.Ok(entries);
    }
}
