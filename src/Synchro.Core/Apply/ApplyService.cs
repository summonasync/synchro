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
        if (manifest.Mode == SyncMode.Git && state.Channel.Mode == SyncMode.Plain)
        {
            return new Result<ApplyReport>.Fail(SyncError.Invalid(
                "git-mode package carries sha positions — cannot apply onto a plain (non-repository) store"));
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

        if (manifest.Mode == SyncMode.Plain && IsRedelivery(root, manifest) && manifest.NewPosition is ChainPosition redelivered)
        {
            // redelivery doubles as the ack: record the peer's outgoing position even
            // though there is nothing new to write
            var acked = state with { AppliedChainSeq = Math.Max(state.AppliedChainSeq, redelivered.Seq) };
            var noopReport = AlreadyApplied().Value;
            return new Result<ApplyReport>.Ok(new ApplyReport
            {
                AppliedFiles = noopReport.AppliedFiles,
                AppliedDeletions = noopReport.AppliedDeletions,
                ConflictPaths = noopReport.ConflictPaths,
                Warnings = noopReport.Warnings.Append("package already applied — redelivery recorded").ToList(),
                AlreadyApplied = true,
                ScenePending = false,
                UpdatedState = acked
            });
        }

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
                    Digest = digest,
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
            var (advanced, advanceWarnings) = AdvanceState(root, state, manifest, SyncMode.Git, digest, headSha: headOk.Value);
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
                    $"package builds on chain #{baseSeq}, this store is at chain #{state.AppliedChainSeq} — out-of-order or lost package"));
            }
        }

        var scanned = PlainTree.ComputeHashes(root);
        if (scanned is not Result<Dictionary<string, string>>.Ok scanOk)
        {
            return new Result<ApplyReport>.Fail(((Result<Dictionary<string, string>>.Fail)scanned).Error);
        }

        var incomingByPath = manifest.Files.ToDictionary(f => f.Path, f => f, StringComparer.Ordinal);
        var driftBase = state.SentFileHashes.Count > 0 ? state.SentFileHashes : state.AppliedFileHashes;
        var driftedCandidates = isBaseline
            ? BaselineContentConflicts(root, incomingByPath)
            : LocalDrift(scanOk.Value, driftBase);

        // locally added files absent from the drift base: conflict only when their content
        // differs from the incoming version — identical additions apply silently
        driftedCandidates.ExceptWith(
            incomingByPath.Keys.Where(p => File.Exists(Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar))))
                .Where(p => ContentHash.Compute(File.ReadAllBytes(Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar)))) == incomingByPath[p].Hash));
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
                    Digest = digest,
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
            var (advanced, advanceWarnings) = AdvanceState(root, state, manifest, SyncMode.Plain, digest, headSha: null);
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

    // idempotent redelivery: every incoming file already on disk with the package's exact
    // content and every deletion already gone — the peer re-sent what we already have
    private static bool IsRedelivery(string root, SyncManifest manifest)
    {
        if (manifest.Files.Count == 0 && manifest.Deletions.Count == 0)
        {
            return false;
        }

        foreach (var entry in manifest.Files)
        {
            var localPath = Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(localPath))
            {
                return false;
            }

            try
            {
                if (ContentHash.Compute(File.ReadAllBytes(localPath)) != entry.Hash)
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return false;
            }
        }

        return manifest.Deletions.All(d => !File.Exists(Path.Combine(root, d.Replace('/', Path.DirectorySeparatorChar))));
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

    internal static (StoreState Updated, List<string> Warnings) AdvanceState(
        string root,
        StoreState state,
        SyncManifest manifest,
        SyncMode mode,
        string digest,
        string? headSha)
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

        var newChainSeq = mode == SyncMode.Plain && manifest.NewPosition is ChainPosition appliedChain
            ? appliedChain.Seq
            : state.AppliedChainSeq;

        var updated = state with
        {
            Ledger = [.. state.Ledger, evt],
            PeerHeadPosition = mode == SyncMode.Git && manifest.NewPosition is GitPosition pos
                ? pos.Sha
                : state.PeerHeadPosition,
            PeerKnownLocalPosition = mode == SyncMode.Git
                ? ackSha ?? state.PeerKnownLocalPosition
                : state.PeerKnownLocalPosition,
            SentChainSeq = Math.Max(state.SentChainSeq, newChainSeq),
            AppliedChainSeq = newChainSeq
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

    internal static Dictionary<string, byte[]> SnapshotContents(string root, IEnumerable<string> paths)
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

    private static Result<ApplyReport>.Ok AlreadyApplied() =>
        new(new ApplyReport
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
