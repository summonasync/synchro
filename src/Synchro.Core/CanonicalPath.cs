namespace Synchro.Core;

public static class CanonicalPath
{
    public static string Resolve(string path, string relativeTo)
    {
        var fullPath = Path.GetFullPath(path, relativeTo);
        var root = Path.GetPathRoot(fullPath);
        if (root is null)
        {
            return fullPath;
        }

        var resolved = root;
        foreach (var segment in fullPath[root.Length..].Split(
                     Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = ResolveSegment(Path.Combine(resolved, segment));
        }

        return resolved.Length > 1 ? resolved.TrimEnd(Path.DirectorySeparatorChar) : resolved;
    }

    private static string ResolveSegment(string candidate)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : new FileInfo(candidate);
            return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidate;
        }
        catch (IOException)
        {
            return candidate;
        }
    }
}
