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
}

public static class PackService
{
    public static Result<PackDraft> Pack(string root, StoreState state, PackOptions options)
    {
        if (state.Channel.Mode != SyncMode.Git)
        {
            return new Result<PackDraft>.Fail(
                SyncError.Invalid("pack requires a git-mode store — this directory was initialized without a repository"));
        }

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

        var files = new List<PackageFileEntry>();
        foreach (var path in changesOk.Value.Added.Concat(changesOk.Value.Modified).OrderBy(p => p, StringComparer.Ordinal))
        {
            var content = GitChangeService.GetFileContent(root, path);
            if (content is not Result<byte[]>.Ok contentOk)
            {
                return new Result<PackDraft>.Fail(((Result<byte[]>.Fail)content).Error);
            }

            files.Add(new PackageFileEntry
            {
                Path = path,
                Hash = ContentHash.Compute(contentOk.Value),
                Content = contentOk.Value
            });
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
            Files = files,
            Deletions = [.. changesOk.Value.Deleted]
        };

        return new Result<PackDraft>.Ok(new PackDraft { Manifest = manifest, Warnings = warnings });
    }
}
