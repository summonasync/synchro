namespace Synchro.Core.Install;

public static class LinuxNameLink
{
    public static Result<LinkSwapped> Swap(string linkPath, string targetPath, string layoutRoot, bool force)
    {
        try
        {
            if (!File.Exists(linkPath) && !IsLink(linkPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
                File.CreateSymbolicLink(linkPath, targetPath);
                return new Result<LinkSwapped>.Ok(new LinkSwapped
                {
                    Created = true,
                    RePointed = false,
                    ForcedForeign = false,
                    ForeignTarget = null
                });
            }

            var previous = ResolveTarget(linkPath) ?? linkPath;
            var foreign = !OwnedByLayout(previous, layoutRoot);
            if (foreign && !force)
            {
                return new Result<LinkSwapped>.Fail(SyncError.Conflict(
                    $"'{linkPath}' already points at '{previous}' which is outside the synchro layout — rerun with --force to re-point it"));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
            var staged = Path.Combine(Path.GetDirectoryName(linkPath)!, ".synchro-link-" + Guid.NewGuid().ToString("N"));
            File.CreateSymbolicLink(staged, targetPath);
            File.Move(staged, linkPath, overwrite: true);
            return new Result<LinkSwapped>.Ok(new LinkSwapped
            {
                Created = false,
                RePointed = true,
                ForcedForeign = foreign,
                ForeignTarget = foreign ? previous : null
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<LinkSwapped>.Fail(SyncError.Conflict($"symlink swap failed for '{linkPath}': {ex.Message}"));
        }
    }

    public static bool IsLink(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: false) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string? ResolveTarget(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: false)?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool OwnedByLayout(string? target, string layoutRoot)
    {
        if (target is null)
        {
            return false;
        }

        var prefix = layoutRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return target.StartsWith(prefix, StringComparison.Ordinal);
    }
}
