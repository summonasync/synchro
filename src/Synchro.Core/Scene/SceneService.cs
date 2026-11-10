using Synchro.Core;
using Synchro.Core.Apply;
using Synchro.Core.Git;
using Synchro.Core.Package;
using Synchro.Core.Store;

namespace Synchro.Core.Scene;

public sealed record SceneStatus(
    string SceneDir,
    IReadOnlyList<string> ConflictPaths,
    IReadOnlyDictionary<string, string> Resolutions,
    IReadOnlyList<string> Unresolved);

public static class SceneService
{
    public const string OpDrop = "drop";
    public const string OpReplace = "replace";
    public const string OpMerge = "merge";
    public const string OpResolved = "resolved";

    public static readonly string[] Ops = [OpDrop, OpReplace, OpMerge, OpResolved];

    public static Result<SceneStatus> Status(StoreState state)
    {
        if (state.PendingScene is null)
        {
            return new Result<SceneStatus>.Fail(SyncError.Conflict("no pending conflict scene"));
        }

        var scene = state.PendingScene;
        var unresolved = scene.ConflictPaths.Where(p => !scene.Resolutions.ContainsKey(p)).ToList();
        return new Result<SceneStatus>.Ok(new SceneStatus(scene.SceneDir, scene.ConflictPaths, scene.Resolutions, unresolved));
    }

    public static Result<StoreState> Resolve(string root, StoreState state, string path, string op)
    {
        if (state.PendingScene is null)
        {
            return new Result<StoreState>.Fail(SyncError.Conflict("no pending conflict scene"));
        }

        var scene = state.PendingScene;
        if (!scene.ConflictPaths.Contains(path))
        {
            return new Result<StoreState>.Fail(SyncError.Invalid($"'{path}' is not part of this scene"));
        }

        if (scene.Resolutions.TryGetValue(path, out var existing))
        {
            // finalizing a merge draft re-resolves the path — anything else is a double resolve
            if (op != OpResolved || existing != OpMerge)
            {
                return new Result<StoreState>.Fail(SyncError.Conflict($"'{path}' is already resolved as '{existing}'"));
            }
        }

        var separator = Path.DirectorySeparatorChar;
        var theirsPath = Path.Combine(scene.SceneDir, "theirs", path.Replace('/', separator));
        var basePath = Path.Combine(scene.SceneDir, "base", path.Replace('/', separator));
        var localPath = Path.Combine(root, path.Replace('/', separator));

        switch (op)
        {
            case OpDrop:
                break;

            case OpReplace:
                ApplyReplace(theirsPath, localPath);
                break;

            case OpMerge:
                if (!File.Exists(theirsPath) || !File.Exists(basePath))
                {
                    return new Result<StoreState>.Fail(SyncError.Invalid(
                        $"merge needs theirs and base versions — '{path}' has none (deletion conflict): drop or replace"));
                }

                if (!File.Exists(localPath))
                {
                    return new Result<StoreState>.Fail(SyncError.Invalid($"local '{path}' is missing — cannot merge"));
                }

                // fixed labels: git merge-file would otherwise stamp absolute local paths
                // into the conflict markers — a trace leak into the user's working file
                var merge = GitRunner.Run(
                    root,
                    "merge-file",
                    "-L", "ours",
                    "-L", "base",
                    "-L", "theirs",
                    localPath, basePath, theirsPath);
                if (merge.ExitCode == 255)
                {
                    return new Result<StoreState>.Fail(SyncError.Invalid($"git merge-file failed: {merge.StdErr.Trim()}"));
                }

                break;

            case OpResolved:
                if (!scene.Resolutions.TryGetValue(path, out var prior) || prior != OpMerge)
                {
                    return new Result<StoreState>.Fail(SyncError.Invalid(
                        $"'resolved' finalizes merge drafts only — resolve '{path}' with merge first"));
                }

                if (File.Exists(localPath) && CarriesMarkers(localPath))
                {
                    return new Result<StoreState>.Fail(SyncError.Invalid(
                        $"'{path}' still carries conflict markers — resolve them in the file first"));
                }

                break;

            default:
                return new Result<StoreState>.Fail(SyncError.Invalid(
                    $"unknown op '{op}' — use {string.Join(" | ", Ops)}"));
        }

        var resolutions = new Dictionary<string, string>(scene.Resolutions, StringComparer.Ordinal) { [path] = op };
        var updated = state with { PendingScene = scene with { Resolutions = resolutions } };
        return new Result<StoreState>.Ok(updated);
    }

    public static Result<ApplyReport> Commit(string root, StoreState state, byte[] key)
    {
        if (state.PendingScene is null)
        {
            return new Result<ApplyReport>.Fail(SyncError.Conflict("no pending conflict scene"));
        }

        var scene = state.PendingScene;
        var unresolved = scene.ConflictPaths.Where(p => !scene.Resolutions.ContainsKey(p)).ToList();
        if (unresolved.Count > 0)
        {
            return new Result<ApplyReport>.Fail(SyncError.Conflict(
                $"{unresolved.Count} conflict path(s) unresolved: {string.Join(", ", unresolved)}"));
        }

        if (!File.Exists(scene.PackagePath))
        {
            return new Result<ApplyReport>.Fail(SyncError.Conflict(
                $"package file moved or deleted — restore '{scene.PackagePath}' to finalize the scene"));
        }

        var opened = PackageSealer.Open(scene.PackagePath, key);
        if (opened is not Result<SyncManifest>.Ok manifestOk)
        {
            return new Result<ApplyReport>.Fail(((Result<SyncManifest>.Fail)opened).Error);
        }

        var manifest = manifestOk.Value;
        var digest = SceneStaging.Digest(scene.PackagePath);
        var headSha = manifest.Mode == SyncMode.Git
            ? GitChangeService.GetHeadSha(root) is Result<string>.Ok head ? head.Value : null
            : null;
        var baseSha = manifest.BasePosition is GitPosition basePos ? basePos.Sha : null;

        var (advanced, warnings) = ApplyService.AdvanceState(
            root, state, manifest, manifest.Mode, digest, headSha, baseSha);

        var updated = advanced with { PendingScene = null };
        try
        {
            Directory.Delete(scene.SceneDir, true);
        }
        catch (IOException)
        {
        }

        return new Result<ApplyReport>.Ok(new ApplyReport
        {
            AppliedFiles = [],
            AppliedDeletions = [],
            ConflictPaths = scene.ConflictPaths,
            Warnings = warnings,
            AlreadyApplied = false,
            ScenePending = false,
            UpdatedState = updated
        });
    }

    private static void ApplyReplace(string theirsPath, string localPath)
    {
        if (File.Exists(theirsPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            File.Copy(theirsPath, localPath, overwrite: true);
            return;
        }

        if (File.Exists(localPath))
        {
            File.Delete(localPath);
        }
    }

    private static bool CarriesMarkers(string path)
    {
        var content = File.ReadAllText(path);
        return content.Contains("<<<<<<<", StringComparison.Ordinal)
               || content.Contains(">>>>>>>", StringComparison.Ordinal);
    }
}
