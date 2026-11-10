using System.Security.Cryptography;
using System.Text;
using Synchro.Core.Hashing;

namespace Synchro.Core.Pack;

public static class PlainTree
{
    private static readonly string[] SkipDirs = [".synchro", ".git"];

    public static Dictionary<string, string> ComputeHashes(string root)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in EnumerateFiles(root))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            hashes[relative] = ContentHash.Compute(File.ReadAllBytes(file));
        }

        return hashes;
    }

    public static byte[] ReadFile(string root, string relativePath) =>
        File.ReadAllBytes(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public static string ComputeRootHash(Dictionary<string, string> hashes) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", hashes.OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => $"{kvp.Key}:{kvp.Value}")))));

    private static IEnumerable<string> EnumerateFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var relative = Path.GetRelativePath(root, f);
                return !IsSkippedDir(relative) && !relative.EndsWith(".synchr", StringComparison.Ordinal);
            });

    private static bool IsSkippedDir(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => SkipDirs.Contains(p));
    }
}
