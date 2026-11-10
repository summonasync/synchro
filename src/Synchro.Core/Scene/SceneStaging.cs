using Synchro.Core.Git;
using Synchro.Core.Store;

namespace Synchro.Core.Scene;

public static class SceneStaging
{
    public static string Stage(
        string root,
        SyncManifest manifest,
        IReadOnlyList<string> conflictPaths,
        string digest,
        string? baseSha,
        IReadOnlyDictionary<string, byte[]>? baseContents)
    {
        var sceneDir = Path.Combine(root, StorePaths.FolderName, "scene", digest);
        var theirsDir = Path.Combine(sceneDir, "theirs");
        var baseDir = Path.Combine(sceneDir, "base");

        var incoming = manifest.Files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);
        foreach (var path in conflictPaths)
        {
            if (incoming.TryGetValue(path, out var content))
            {
                WriteUnder(theirsDir, path, content);
            }

            var baseVersion = ResolveBase(root, path, baseSha, baseContents);
            if (baseVersion is not null)
            {
                WriteUnder(baseDir, path, baseVersion);
            }
        }

        return sceneDir;
    }

    public static string Digest(string packagePath) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(packagePath)))[..12];

    private static byte[]? ResolveBase(
        string root,
        string path,
        string? baseSha,
        IReadOnlyDictionary<string, byte[]>? baseContents)
    {
        if (baseSha is not null)
        {
            var baseFile = GitChangeService.GetFileContent(root, path, baseSha) is Result<byte[]>.Ok ok
                ? ok.Value
                : null;
            return baseFile ?? FromContents(baseContents, path);
        }

        return FromContents(baseContents, path);
    }

    private static byte[]? FromContents(IReadOnlyDictionary<string, byte[]>? contents, string path) =>
        contents is not null && contents.TryGetValue(path, out var bytes) ? bytes : null;

    private static void WriteUnder(string baseDir, string relativePath, byte[] content)
    {
        var target = Path.Combine(baseDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, content);
    }
}
