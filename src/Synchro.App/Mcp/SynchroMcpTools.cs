using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using Synchro.Core;
using Synchro.Core.Apply;
using Synchro.Core.Pack;
using Synchro.Core.Package;
using Synchro.Core.Scene;
using Synchro.Core.Store;
using Synchro.Core.Git;

namespace Synchro.App.Mcp;

[McpServerToolType]
public class SynchroMcpTools(string root)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private SyncManifest? _draftManifest;
    private bool _draftIsFull;

    private Result<StoreState> LoadStore(out byte[] key)
    {
        key = [];
        if (!Directory.Exists(StorePaths.Folder(root)) || !StoreInitializer.TryLoadKey(root, out key))
        {
            return new Result<StoreState>.Fail(SyncError.Invalid(
                "no synchro store in the target directory — run 'synchro init' first"));
        }

        return StoreContainer.Load(StorePaths.Container(root), key);
    }

    private string JsonError(SyncError error) =>
        JsonSerializer.Serialize(new { ok = false, error = error.Message, code = error.Code }, Json);

    [McpServerTool(Name = "synchro_status")]
    [System.ComponentModel.Description(
        "Synchro store summary: channel, mode, peer positions, sync chain, pending conflict scene, and locally changed (drifted) files since the last sync point")]
    public string Status()
    {
        var loaded = LoadStore(out _);
        if (loaded is not Result<StoreState>.Ok state)
        {
            return JsonError(((Result<StoreState>.Fail)loaded).Error);
        }

        var value = state.Value;
        List<string> drift = [];
        if (value.Channel.Mode == SyncMode.Plain)
        {
            var scanned = PlainTree.ComputeHashes(root);
            if (scanned is Result<Dictionary<string, string>>.Ok scanOk)
            {
                drift = [.. scanOk.Value
                    .Where(kvp => !value.AppliedFileHashes.TryGetValue(kvp.Key, out var h) || h != kvp.Value)
                    .Select(kvp => kvp.Key)];
            }
        }
        else
        {
            var lastApplied = value.Ledger.LastOrDefault(e => e.Direction == LedgerDirection.Applied)?.LocalPosition;
            if (lastApplied is not null &&
                GitChangeService.GetChanges(root, lastApplied) is Result<GitChanges>.Ok changes)
            {
                drift = [.. changes.Value.Added.Concat(changes.Value.Modified).Concat(changes.Value.Deleted)];
            }
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            channel = value.Channel.Name,
            peer = value.Channel.Peer,
            mode = value.Channel.Mode,
            peerHeadPosition = value.PeerHeadPosition,
            peerKnowsUsAt = value.PeerKnownLocalPosition,
            appliedChainSeq = value.AppliedChainSeq,
            ledgerEvents = value.Ledger.Count,
            pendingScene = value.PendingScene?.SceneDir,
            driftedFiles = drift
        }, Json);
    }

    [McpServerTool(Name = "synchro_pack_draft")]
    [System.ComponentModel.Description(
        "Build a package manifest draft from committed work (git-mode: pass full=true for the first baseline; plain-mode: always a delta against the last sent state). Returns the changed files and commit metadata so features can be declared before sealing")]
    public string PackDraft(bool full = false)
    {
        var loaded = LoadStore(out _);
        if (loaded is not Result<StoreState>.Ok state)
        {
            return JsonError(((Result<StoreState>.Fail)loaded).Error);
        }

        var draft = PackService.Pack(root, state.Value, new PackOptions { Full = full });
        if (draft is not Result<PackDraft>.Ok ok)
        {
            _draftManifest = null;
            return JsonError(((Result<PackDraft>.Fail)draft).Error);
        }

        _draftManifest = ok.Value.Manifest;
        _draftIsFull = full;

        return JsonSerializer.Serialize(new
        {
            ok = true,
            mode = ok.Value.Manifest.Mode,
            full = _draftIsFull,
            basePosition = ok.Value.Manifest.BasePosition?.Display,
            newPosition = ok.Value.Manifest.NewPosition.Display,
            files = ok.Value.Manifest.Files.Select(f => f.Path),
            deletions = ok.Value.Manifest.Deletions,
            commits = ok.Value.Manifest.Commits.Select(c => new { c.Sha, c.Message, c.Files }),
            declaredFeatures = ok.Value.Manifest.Features.Select(f => f.Name),
            warnings = ok.Value.Warnings,
            nextStep = "declare features with synchro_pack_declare, then seal with synchro_pack_seal"
        }, Json);
    }

    [McpServerTool(Name = "synchro_pack_declare")]
    [System.ComponentModel.Description(
        "Declare a feature on the cached pack draft: name, human-readable description, and the files it covers (comma-separated paths). Call once per feature; every changed file should belong to some feature before sealing")]
    public string PackDeclare(string name, string description, string files)
    {
        if (_draftManifest is null)
        {
            return JsonError(SyncError.Invalid("no pack draft — call synchro_pack_draft first"));
        }

        var declared = files
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (declared.Count == 0)
        {
            return JsonError(SyncError.Invalid("files must be a comma-separated list of package paths"));
        }

        var known = _draftManifest.Files.Select(f => f.Path)
            .Concat(_draftManifest.Deletions)
            .ToHashSet(StringComparer.Ordinal);
        var unknown = declared.Where(f => !known.Contains(f)).ToList();
        if (unknown.Count > 0)
        {
            return JsonError(SyncError.Invalid(
                $"declared files not present in the draft: {string.Join(", ", unknown)}"));
        }

        if (_draftManifest.Features.Any(f => f.Name == name))
        {
            return JsonError(SyncError.Invalid($"feature '{name}' is already declared"));
        }

        var covered = _draftManifest.Features.SelectMany(f => f.Files).ToHashSet(StringComparer.Ordinal);
        var doubleDeclared = declared.Where(covered.Contains).ToList();
        if (doubleDeclared.Count > 0)
        {
            return JsonError(SyncError.Invalid(
                $"files already covered by another feature: {string.Join(", ", doubleDeclared)}"));
        }

        var features = _draftManifest.Features.ToList();
        features.Add(new FeatureDeclaration
        {
            Name = name,
            Description = description,
            Files = declared
        });
        _draftManifest = _draftManifest with { Features = features };

        return JsonSerializer.Serialize(new
        {
            ok = true,
            features = _draftManifest.Features.Select(f => new { f.Name, f.Files }),
            undeclared = known.Except(_draftManifest.Features.SelectMany(f => f.Files)).ToList(),
            nextStep = "seal with synchro_pack_seal when every file is declared"
        }, Json);
    }

    [McpServerTool(Name = "synchro_pack_seal")]
    [System.ComponentModel.Description(
        "Seal the cached pack draft into one encrypted .synchr package file. Returns the package path to hand to the peer (physical transfer is up to the humans)")]
    public string PackSeal(string? outPath = null)
    {
        if (_draftManifest is null)
        {
            return JsonError(SyncError.Invalid("no pack draft — call synchro_pack_draft first"));
        }

        var loaded = LoadStore(out var key);
        if (loaded is not Result<StoreState>.Ok state)
        {
            return JsonError(((Result<StoreState>.Fail)loaded).Error);
        }

        var target = outPath ?? DefaultPackageName(state.Value, _draftManifest);
        target = Path.IsPathRooted(target) ? target : Path.Combine(root, target);
        if (!target.EndsWith(".synchr", StringComparison.OrdinalIgnoreCase))
        {
            target += ".synchr";
        }

        PackageSealer.Seal(target, key, _draftManifest);
        var manifest = _draftManifest;
        _draftManifest = null;

        return JsonSerializer.Serialize(new
        {
            ok = true,
            package = Path.GetFullPath(target),
            bytes = new FileInfo(target).Length,
            newPosition = manifest.NewPosition.Display,
            files = manifest.Files.Count,
            deletions = manifest.Deletions.Count,
            features = manifest.Features.Select(f => f.Name)
        }, Json);
    }

    [McpServerTool(Name = "synchro_apply_start")]
    [System.ComponentModel.Description(
        "Apply a sealed .synchr package: verifies, checks drift, silently applies non-overlapping features, and stages overlapping or similar ones into a conflict scene. Read the returned report before touching the scene")]
    public string ApplyStart(string packagePath)
    {
        packagePath = Path.IsPathRooted(packagePath) ? packagePath : Path.Combine(root, packagePath);
        var loaded = LoadStore(out var key);
        if (loaded is not Result<StoreState>.Ok state)
        {
            return JsonError(((Result<StoreState>.Fail)loaded).Error);
        }

        var report = ApplyService.Apply(root, state.Value, key, packagePath);
        if (report is not Result<ApplyReport>.Ok ok)
        {
            return JsonError(((Result<ApplyReport>.Fail)report).Error);
        }

        if (ok.Value.UpdatedState is { } updated)
        {
            StoreContainer.Save(StorePaths.Container(root), key, updated);
            // the draft was built on a position this apply may have moved forward
            _draftManifest = null;
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            alreadyApplied = ok.Value.AlreadyApplied,
            appliedFiles = ok.Value.AppliedFiles,
            appliedDeletions = ok.Value.AppliedDeletions,
            conflicts = ok.Value.ConflictPaths,
            scenePending = ok.Value.ScenePending,
            warnings = ok.Value.Warnings,
            nextStep = ok.Value.ScenePending
                ? "inspect the scene with synchro_scene_status and resolve each path"
                : "package fully applied"
        }, Json);
    }

    [McpServerTool(Name = "synchro_scene_status")]
    [System.ComponentModel.Description(
        "Pending conflict scene summary: every conflicted path, its resolution state, and the scene directory holding theirs/base versions")]
    public string SceneStatus()
    {
        var loaded = LoadStore(out _);
        if (loaded is not Result<StoreState>.Ok state)
        {
            return JsonError(((Result<StoreState>.Fail)loaded).Error);
        }

        var status = SceneService.Status(state.Value);
        if (status is not Result<SceneStatus>.Ok ok)
        {
            return JsonError(((Result<SceneStatus>.Fail)status).Error);
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            sceneDir = ok.Value.SceneDir,
            conflicts = ok.Value.ConflictPaths.Select(p => new
            {
                path = p,
                resolution = ok.Value.Resolutions.TryGetValue(p, out var r) ? r : "unresolved"
            }),
            unresolved = ok.Value.Unresolved
        }, Json);
    }

    [McpServerTool(Name = "synchro_scene_read")]
    [System.ComponentModel.Description(
        "Read one conflicted path in all three versions: theirs (from the package), base (common ancestor), ours (working tree). Use this to understand the conflict before resolving")]
    public string SceneRead(string path)
    {
        var loaded = LoadStore(out _);
        if (loaded is not Result<StoreState>.Ok state)
        {
            return JsonError(((Result<StoreState>.Fail)loaded).Error);
        }

        var scene = state.Value.PendingScene;
        if (scene is null)
        {
            return JsonError(SyncError.Conflict("no pending conflict scene"));
        }

        if (!scene.ConflictPaths.Contains(path))
        {
            return JsonError(SyncError.Invalid($"'{path}' is not part of this scene"));
        }

        string? ReadSafe(string baseDir)
        {
            var file = Path.Combine(baseDir, path.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }

        var theirs = ReadSafe(Path.Combine(scene.SceneDir, "theirs"));
        var baseVersion = ReadSafe(Path.Combine(scene.SceneDir, "base"));
        var oursFile = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        var ours = File.Exists(oursFile) ? File.ReadAllText(oursFile) : null;

        return JsonSerializer.Serialize(new
        {
            ok = true,
            path,
            resolution = scene.Resolutions.TryGetValue(path, out var r) ? r : "unresolved",
            theirs,
            baseVersion,
            ours,
            hint = "resolve with synchro_scene_resolve: drop keeps ours, replace takes theirs, merge writes a marker draft into the working file (finalize with the resolved op after editing)"
        }, Json);
    }

    [McpServerTool(Name = "synchro_scene_resolve")]
    [System.ComponentModel.Description(
        "Resolve one conflicted path: drop (keep ours), replace (take theirs; deletion-conflict deletes local), merge (write a three-way marker draft into the working file), resolved (finalize an edited merge draft — fails while conflict markers remain)")]
    public string SceneResolve(string path, string op)
    {
        var loaded = LoadStore(out var key);
        if (loaded is not Result<StoreState>.Ok state)
        {
            return JsonError(((Result<StoreState>.Fail)loaded).Error);
        }

        var resolved = SceneService.Resolve(root, state.Value, path, op);
        if (resolved is not Result<StoreState>.Ok ok)
        {
            return JsonError(((Result<StoreState>.Fail)resolved).Error);
        }

        StoreContainer.Save(StorePaths.Container(root), key, ok.Value);
        _draftManifest = null;
        var unresolved = SceneService.Status(ok.Value) is Result<SceneStatus>.Ok s
            ? s.Value.Unresolved
            : [];

        return JsonSerializer.Serialize(new
        {
            ok = true,
            path,
            op,
            unresolved,
            nextStep = unresolved.Count == 0
                ? "finalize with synchro_apply_commit"
                : $"resolve the remaining {unresolved.Count} path(s)"
        }, Json);
    }

    [McpServerTool(Name = "synchro_apply_commit")]
    [System.ComponentModel.Description(
        "Finalize a fully resolved conflict scene: advances the sync state (ledger, chain/positions) and cleans the scene. Requires every conflicted path resolved")]
    public string ApplyCommit()
    {
        var loaded = LoadStore(out var key);
        if (loaded is not Result<StoreState>.Ok state)
        {
            return JsonError(((Result<StoreState>.Fail)loaded).Error);
        }

        var committed = SceneService.Commit(root, state.Value, key);
        if (committed is not Result<ApplyReport>.Ok ok)
        {
            return JsonError(((Result<ApplyReport>.Fail)committed).Error);
        }

        if (ok.Value.UpdatedState is { } updated)
        {
            StoreContainer.Save(StorePaths.Container(root), key, updated);
            _draftManifest = null;
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            resolvedPaths = ok.Value.ConflictPaths,
            warnings = ok.Value.Warnings
        }, Json);
    }

    [McpServerTool(Name = "synchro_log")]
    [System.ComponentModel.Description("Sync history: the alignment ledger events (sent/applied) of this store")]
    public string Log()
    {
        var loaded = LoadStore(out _);
        if (loaded is not Result<StoreState>.Ok state)
        {
            return JsonError(((Result<StoreState>.Fail)loaded).Error);
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            events = state.Value.Ledger.Select(e => new
            {
                e.AtUtc,
                direction = e.Direction,
                localPosition = e.LocalPosition,
                peerPosition = e.PeerPosition,
                packageDigest = e.PackageDigest
            })
        }, Json);
    }

    private string DefaultPackageName(StoreState state, SyncManifest manifest) =>
        manifest.Mode == SyncMode.Plain && manifest.NewPosition is ChainPosition chain
            ? $"synchro-{chain.Seq}-{Sanitize(state.Channel.Peer)}.synchr"
            : $"synchro-{manifest.NewPosition.Display[..Math.Min(8, manifest.NewPosition.Display.Length)]}-{Sanitize(state.Channel.Peer)}.synchr";

    private static string Sanitize(string name) =>
        new(name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
}
