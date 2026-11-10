using Synchro.Core;

namespace Synchro.Core.Package;

public static class PackagePaths
{
    // packages travel over untrusted transports; every manifest path is hostile until validated
    public static SyncError? Validate(string root, IEnumerable<string> paths)
    {
        var rootFull = Path.GetFullPath(root);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return SyncError.Invalid("package contains an empty path");
            }

            if (path.IndexOf('\0') >= 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return SyncError.Invalid($"invalid characters in path: '{path}'");
            }

            if (Path.IsPathRooted(path))
            {
                return SyncError.Invalid($"rooted paths are not allowed: '{path}'");
            }

            if (path.Split('/', '\\').Any(s => s == ".."))
            {
                return SyncError.Invalid($"parent segments are not allowed: '{path}'");
            }

            var full = Path.GetFullPath(Path.Combine(rootFull, path));
            if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return SyncError.Invalid($"path escapes the store root: '{path}'");
            }
        }

        return null;
    }

    public static bool IsSafe(string root, string path) => Validate(root, [path]) is null;
}
