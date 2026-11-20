namespace Synchro.Core.Install;

public static class LinuxPathFix
{
    public const string StartMarker = "# >>> synchro >>>";
    public const string EndMarker = "# <<< synchro <<<";

    public static string Block(string binDir) => $"{StartMarker}\nexport PATH=\"{binDir}:$PATH\"\n{EndMarker}\n";

    public static bool HasBlock(string content) =>
        content.Contains(StartMarker, StringComparison.Ordinal) && content.Contains(EndMarker, StringComparison.Ordinal);

    public static Result<bool> Append(string profilePath, string binDir)
    {
        try
        {
            var current = File.Exists(profilePath) ? File.ReadAllText(profilePath) : string.Empty;
            if (HasBlock(current))
            {
                return new Result<bool>.Ok(false);
            }

            var prefix = current.Length > 0 && !current.EndsWith('\n') ? "\n" : string.Empty;
            File.WriteAllText(profilePath, current + prefix + Block(binDir));
            return new Result<bool>.Ok(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<bool>.Fail(SyncError.Conflict($"could not update '{profilePath}': {ex.Message}"));
        }
    }

    public static Result<bool> Remove(string profilePath)
    {
        try
        {
            if (!File.Exists(profilePath))
            {
                return new Result<bool>.Ok(false);
            }

            var current = File.ReadAllText(profilePath);
            var start = current.IndexOf(StartMarker, StringComparison.Ordinal);
            var end = current.IndexOf(EndMarker, StringComparison.Ordinal);
            if (start < 0 || end < 0 || end < start)
            {
                return new Result<bool>.Ok(false);
            }

            var cutEnd = end + EndMarker.Length;
            if (cutEnd < current.Length && current[cutEnd] == '\n')
            {
                cutEnd++;
            }

            File.WriteAllText(profilePath, current[..start] + current[cutEnd..]);
            return new Result<bool>.Ok(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<bool>.Fail(SyncError.Conflict($"could not update '{profilePath}': {ex.Message}"));
        }
    }
}
