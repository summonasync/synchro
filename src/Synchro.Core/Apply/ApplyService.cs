using Synchro.Core.Git;
using Synchro.Core.Hashing;
using Synchro.Core.Pack;
using Synchro.Core.Package;
using Synchro.Core.Scene;
using Synchro.Core.Store;

namespace Synchro.Core.Apply;

public static class ApplyService
{
    public static Result<ApplyReport> Apply(string root, StoreState state, byte[] key, string packagePath)
    {
        var opened = PackageSealer.Open(packagePath, key);
        if (opened is not Result<SyncManifest>.Ok manifestOk)
        {
            return new Result<ApplyReport>.Fail(((Result<SyncManifest>.Fail)opened).Error);
        }

        var manifest = manifestOk.Value;
        if (manifest.Mode != state.Channel.Mode)
        {
            return new Result<ApplyReport>.Fail(
                SyncError.Invalid($"package mode '{manifest.Mode}' does not match store mode '{state.Channel.Mode}'"));
        }

        var pathError = PackagePaths.Validate(
            root,
            manifest.Files.Select(f => f.Path).Concat(manifest.Deletions));
        if (pathError is not null)
        {
            return new Result<ApplyReport>.Fail(pathError);
        }

        if (state.PendingScene is not null)
        {
            return new Result<ApplyReport>.Fail(
                SyncError.Conflict($"pending conflict scene at '{state.PendingScene.SceneDir}' — resolve it before applying"));
        }

        var digest = SceneStaging.Digest(packagePath);
        return manifest.Mode == SyncMode.Git
            ? ApplyGit(root, state, manifest, packagePath, digest)
            : ApplyPlain(root, state, manifest, packagePath, digest);
    }

    private static Result<ApplyReport> ApplyGit(
        string root, StoreState state, SyncManifest manifest, string packagePath, string digest)
    {
        var head = GitChangeService.GetHeadSha(root);
        if (head is not Result<string>.Ok headOk)
        {
            return new Result<ApplyReport>.Fail(((Result<string>.Fail)head).Error);
        }

        string? baseSha = null;
        var myChanged = new HashSet<string>(StringComparer.Ordinal);

        if (manifest.NewPosition is GitPosition repeated && state.PeerHeadPosition is not null && repeated.Sha == state.PeerHeadPosition)
        {
            return AlreadyApplied();
        }

        if (manifest.BasePosition is null)
        {
            if (state.PeerHeadPosition is not null || state.PeerKnownLocalPosition is not null || state.AppliedFileHashes.Count > 0)
            {
                return new Result<ApplyReport>.Fail(SyncError.Conflict("baseline already applied to this store"));
            }
        }
        else
        {
            if (manifest.BasePosition is not GitPosition basePos)
            {
                return new Result<ApplyReport>.Fail(SyncError.Invalid("git-mode package carries a non-git base position"));
            }

            if (manifest.NewPosition is not GitPosition)
            {
                return new Result<ApplyReport>.Fail(SyncError.Invalid("git-mode package carries a non-git new position"));
            }

            baseSha = basePos.Sha;

            if (state.PeerHeadPosition is null)
            {
                return new Result<ApplyReport>.Fail(SyncError.Conflict(
                    "no baseline applied yet — apply the baseline package first"));
            }

            if (baseSha != state.PeerHeadPosition)
            {
                return new Result<ApplyReport>.Fail(SyncError.Conflict(
                    $"package builds on {baseSha[..12]}, but this store knows the peer at {state.PeerHeadPosition[..12]} — out-of-order or lost package: apply the missing package first"));
            }

            var lastAppliedLocal = state.Ledger
                .LastOrDefault(e => e.Direction == LedgerDirection.Applied)?.LocalPosition;
            var changed = LocalChangedSet(root, lastAppliedLocal);
            if (changed is not Result<HashSet<string>>.Ok changedOk)
            {
                return new Result<ApplyReport>.Fail(((Result<HashSet<string>>.Fail)changed).Error);
            }

            myChanged = changedOk.Value;
        }

        var incomingByPath = manifest.Files.ToDictionary(f => f.Path, f => f, StringComparer.Ordinal);
        var changedCandidates = baseSha is null
            ? BaselineContentConflicts(root, incomingByPath)
            : [.. incomingByPath.Keys.Where(p => myChanged.Contains(p))];
        // a mid-apply failure leaves already-written files drifted/changed; the disk already
        // carries the package content, so re-apply must treat them as applied, not conflicts
        var overlap = changedCandidates
            .Except(AlreadyPackageContent(root, incomingByPath, changedCandidates))
            .ToHashSet(StringComparer.Ordinal);

        var warnings = new List<string>();
        if (GitChangeService.IsDirty(root))
        {
            warnings.Add("local uncommitted changes present");
        }

        var appliedFiles = new List<string>();
        var appliedDeletions = new List<string>();
        var deletionConflicts = new List<string>();

        try
        {
            // writes before deletions: a mid-apply failure leaves the tree partially
            // written but state unadvanced — re-apply recognises written files via
            // content comparison and finishes the rest
            foreach (var entry in incomingByPath.Values)
            {
                if (overlap.Contains(entry.Path))
                {
                    continue;
                }

                var target = Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, entry.Content);
                appliedFiles.Add(entry.Path);
            }

            foreach (var del in manifest.Deletions)
            {
                var localPath = Path.Combine(root, del.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(localPath))
                {
                    continue;
                }

                if (myChanged.Contains(del))
                {
                    deletionConflicts.Add(del);
                    continue;
                }

                File.Delete(localPath);
                appliedDeletions.Add(del);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<ApplyReport>.Fail(SyncError.Invalid(
                $"apply failed midway ({ex.Message}) — fix the blocker and re-apply; already-written files are recognised as applied"));
        }

        var conflictPaths = overlap
            .Concat(deletionConflicts)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        StoreState updated;
        var scenePending = false;

        if (conflictPaths.Count > 0)
        {
            var localBaseSha = state.Ledger
                .LastOrDefault(e => e.Direction == LedgerDirection.Applied)?.LocalPosition;
            var sceneDir = SceneStaging.Stage(root, manifest, conflictPaths, digest, localBaseSha, state.AppliedContents);
            updated = state with
            {
                PendingScene = new PendingSceneState
                {
                    PackagePath = Path.GetFullPath(packagePath),
                    SceneDir = sceneDir,
                    ConflictPaths = conflictPaths,
                    NewPosition = manifest.NewPosition.Display,
                    BasePosition = baseSha
                }
            };
            scenePending = true;
        }
        else
        {
            var (advanced, advanceWarnings) = AdvanceState(root, state, manifest, SyncMode.Git, digest, headSha: headOk.Value, baseSha: baseSha);
            warnings.AddRange(advanceWarnings);
            updated = advanced;
        }

        return new Result<ApplyReport>.Ok(new ApplyReport
        {
            AppliedFiles = appliedFiles,
            AppliedDeletions = appliedDeletions,
            ConflictPaths = conflictPaths,
            Warnings = warnings,
            AlreadyApplied = false,
            ScenePending = scenePending,
            UpdatedState = updated
        });
    }

    private static Result<ApplyReport> ApplyPlain(
        string root, StoreState state, SyncManifest manifest, string packagePath, string digest)
    {
        if (manifest.NewPosition is not ChainPosition chain)
        {
            return new Result<ApplyReport>.Fail(SyncError.Invalid("plain-mode package carries a non-chain position"));
        }

        var isBaseline = manifest.BasePosition is null;
        if (chain.Seq == state.AppliedChainSeq && state.AppliedChainSeq != 0)
        {
            return AlreadyApplied();
        }

        if (isBaseline && state.AppliedChainSeq != 0)
        {
            return new Result<ApplyReport>.Fail(SyncError.Conflict("baseline already applied to this store"));
        }

        if (!isBaseline)
        {
            var baseSeq = manifest.BasePosition is ChainPosition baseChain ? baseChain.Seq : -1;
            if (baseSeq != state.AppliedChainSeq)
            {
                return new Result<ApplyReport>.Fail(SyncError.Conflict(
                    $"package builds on chain #{baseSeq}, this store is at #{state.AppliedChainSeq} — out-of-order or lost package"));
            }
        }

        var scanned = PlainTree.ComputeHashes(root);
        if (scanned is not Result<Dictionary<string, string>>.Ok scanOk)
        {
            return new Result<ApplyReport>.Fail(((Result<Dictionary<string, string>>.Fail)scanned).Error);
        }

        var incomingByPath = manifest.Files.ToDictionary(f => f.Path, f => f, StringComparer.Ordinal);
        var driftedCandidates = isBaseline
            ? BaselineContentConflicts(root, incomingByPath)
            : LocalDrift(scanOk.Value, state.AppliedFileHashes);
        // re-apply after a mid-apply failure: files already carrying the package content
        // are recognised as applied instead of escalating to a phantom conflict
        var drifted = driftedCandidates
            .Except(AlreadyPackageContent(root, incomingByPath, driftedCandidates.Where(incomingByPath.ContainsKey)))
            .ToHashSet(StringComparer.Ordinal);

        var appliedFiles = new List<string>();
        var appliedDeletions = new List<string>();
        var deletionConflicts = new List<string>();
        var warnings = new List<string>();

        try
        {
            // writes before deletions: re-apply heals a mid-apply failure idempotently
            foreach (var entry in incomingByPath.Values)
            {
                if (drifted.Contains(entry.Path))
                {
                    continue;
                }

                var target = Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, entry.Content);
                appliedFiles.Add(entry.Path);
            }

            foreach (var del in manifest.Deletions)
            {
                var localPath = Path.Combine(root, del.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(localPath))
                {
                    continue;
                }

                if (drifted.Contains(del))
                {
                    deletionConflicts.Add(del);
                    continue;
                }

                File.Delete(localPath);
                appliedDeletions.Add(del);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<ApplyReport>.Fail(SyncError.Invalid(
                $"apply failed midway ({ex.Message}) — fix the blocker and re-apply; already-written files are recognised as applied"));
        }

        var conflictPaths = drifted
            .Where(p => incomingByPath.ContainsKey(p) || manifest.Deletions.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        StoreState updated;
        var scenePending = false;

        if (conflictPaths.Count > 0)
        {
            var sceneDir = SceneStaging.Stage(root, manifest, conflictPaths, digest, baseSha: null, baseContents: state.AppliedContents);
            updated = state with
            {
                PendingScene = new PendingSceneState
                {
                    PackagePath = Path.GetFullPath(packagePath),
                    SceneDir = sceneDir,
                    ConflictPaths = conflictPaths,
                    NewPosition = manifest.NewPosition.Display,
                    BasePosition = manifest.BasePosition?.Display
                }
            };
            scenePending = true;
        }
        else
        {
            var (advanced, advanceWarnings) = AdvanceState(root, state, manifest, SyncMode.Plain, digest, headSha: null, baseSha: null);
            warnings.AddRange(advanceWarnings);
            updated = advanced;
        }

        return new Result<ApplyReport>.Ok(new ApplyReport
        {
            AppliedFiles = appliedFiles,
            AppliedDeletions = appliedDeletions,
            ConflictPaths = conflictPaths,
            Warnings = warnings,
            AlreadyApplied = false,
            ScenePending = scenePending,
            UpdatedState = updated
        });
    }

    private static HashSet<string> AlreadyPackageContent(
        string root,
        Dictionary<string, PackageFileEntry> incoming,
        IEnumerable<string> candidates)
    {
        var already = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in candidates)
        {
            if (!incoming.TryGetValue(path, out var entry))
            {
                continue;
            }

            var localPath = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(localPath))
            {
                continue;
            }

            try
            {
                if (ContentHash.Compute(File.ReadAllBytes(localPath)) == entry.Hash)
                {
                    already.Add(path);
                }
            }
            catch (IOException)
            {
            }
        }

        return already;
    }

    private static HashSet<string> LocalDrift(
        Dictionary<string, string> current, Dictionary<string, string> baseline)
    {
        var drifted = current
            .Where(kvp => !baseline.TryGetValue(kvp.Key, out var hash) || hash != kvp.Value)
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var gone in baseline.Keys.Except(current.Keys))
        {
            drifted.Add(gone);
        }

        return drifted;
    }

    private static HashSet<string> BaselineContentConflicts(
        string root, Dictionary<string, PackageFileEntry> incoming)
    {
        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, entry) in incoming)
        {
            var localPath = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(localPath))
            {
                continue;
            }

            try
            {
                if (ContentHash.Compute(File.ReadAllBytes(localPath)) != entry.Hash)
                {
                    conflicts.Add(path);
                }
            }
            catch (IOException)
            {
                conflicts.Add(path);
            }
        }

        return conflicts;
    }

    private static (StoreState Updated, List<string> Warnings) AdvanceState(
        string root,
        StoreState state,
        SyncManifest manifest,
        SyncMode mode,
        string digest,
        string? headSha,
        string? baseSha)
    {
        var evt = new LedgerEvent
        {
            AtUtc = DateTimeOffset.UtcNow,
            Direction = LedgerDirection.Applied,
            LocalPosition = mode == SyncMode.Git
                ? headSha
                : ((ChainPosition)manifest.NewPosition).Seq.ToString(),
            PeerPosition = manifest.NewPosition.Display,
            PackageDigest = digest
        };

        var ackSha = mode == SyncMode.Git && manifest.AckPosition is GitPosition ack ? ack.Sha : null;

        var updated = state with
        {
            Ledger = [.. state.Ledger, evt],
            PeerHeadPosition = mode == SyncMode.Git && manifest.NewPosition is GitPosition pos
                ? pos.Sha
                : state.PeerHeadPosition,
            PeerKnownLocalPosition = mode == SyncMode.Git
                ? ackSha ?? state.PeerKnownLocalPosition
                : state.PeerKnownLocalPosition,
            AppliedChainSeq = mode == SyncMode.Plain && manifest.NewPosition is ChainPosition chain
                ? chain.Seq
                : state.AppliedChainSeq
        };

        var warnings = new List<string>();
        var scanned = PlainTree.ComputeHashes(root);
        if (scanned is not Result<Dictionary<string, string>>.Ok ok)
        {
            warnings.Add("post-apply tree scan failed — the drift baseline is stale and the next apply may over-conflict");
        }
        else
        {
            updated = updated with
            {
                AppliedFileHashes = ok.Value,
                AppliedContents = SnapshotContents(root, ok.Value.Keys)
            };
        }

        return (updated, warnings);
    }

    private static Dictionary<string, byte[]> SnapshotContents(string root, IEnumerable<string> paths)
    {
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            try
            {
                contents[path] = PlainTree.ReadFile(root, path);
            }
            catch (IOException)
            {
            }
        }

        return contents;
    }

    private static Result<ApplyReport> AlreadyApplied() =>
        new Result<ApplyReport>.Ok(new ApplyReport
        {
            AppliedFiles = [],
            AppliedDeletions = [],
            ConflictPaths = [],
            Warnings = ["package already applied — nothing to do"],
            AlreadyApplied = true,
            ScenePending = false,
            UpdatedState = null
        });

    private static Result<HashSet<string>> LocalChangedSet(string root, string? baseSha)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);

        if (baseSha is not null)
        {
            var diff = GitChangeService.GetChanges(root, baseSha);
            if (diff is not Result<GitChanges>.Ok ok)
            {
                return new Result<HashSet<string>>.Fail(((Result<GitChanges>.Fail)diff).Error);
            }

            foreach (var path in ok.Value.Added.Concat(ok.Value.Modified).Concat(ok.Value.Deleted))
            {
                changed.Add(path);
            }
        }

        var porcelain = GitRunner.Run(root, "status", "--porcelain", "-z").StdOut;
        var tokens = porcelain.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Length <= 3)
            {
                continue;
            }

            var status = token[..2];
            changed.Add(token[3..]);

            if (status[0] is 'R' or 'C' && i + 1 < tokens.Length)
            {
                changed.Add(tokens[i + 1]);
                i++;
            }
        }

        return new Result<HashSet<string>>.Ok(changed);
    }
}
