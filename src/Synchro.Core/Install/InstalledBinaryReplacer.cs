namespace Synchro.Core.Install;

public static class InstalledBinaryReplacer
{
    private const string NewSuffix = ".new";
    private const string OldSuffix = ".old";

    public static Result<ReplaceOutcome> Replace(string incomingPath, string targetPath)
    {
        try
        {
            CleanupStale(targetPath);
            var staged = targetPath + NewSuffix;
            File.Copy(incomingPath, staged, overwrite: true);

            if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            {
                File.Move(staged, targetPath);
                return Placed(cleanupPending: !DeleteAny(targetPath + OldSuffix));
            }

            try
            {
                File.Move(staged, targetPath, overwrite: true);
                return Placed();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var retired = targetPath + OldSuffix;
                MoveAny(targetPath, retired);
                File.Move(staged, targetPath);
                return Placed(fallback: true, cleanupPending: !DeleteAny(retired));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<ReplaceOutcome>.Fail(SyncError.Conflict($"binary replace failed for '{targetPath}': {ex.Message}"));
        }
    }

    private static void MoveAny(string sourcePath, string destinationPath)
    {
        if (Directory.Exists(sourcePath))
        {
            Directory.Move(sourcePath, destinationPath);
        }
        else
        {
            File.Move(sourcePath, destinationPath);
        }
    }

    private static Result<ReplaceOutcome> Placed(bool fallback = false, bool cleanupPending = false) =>
        new Result<ReplaceOutcome>.Ok(new ReplaceOutcome
        {
            UsedLockedTargetFallback = fallback,
            OldCleanupPending = cleanupPending
        });

    private static void CleanupStale(string targetPath)
    {
        DeleteAny(targetPath + NewSuffix);
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            DeleteAny(targetPath + OldSuffix);
        }
    }

    private static bool DeleteAny(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
