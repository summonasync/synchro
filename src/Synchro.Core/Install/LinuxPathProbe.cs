namespace Synchro.Core.Install;

public static class LinuxPathProbe
{
    public static bool Contains(string? pathValue, string directory)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        return pathValue
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(entry => string.Equals(entry, directory, StringComparison.Ordinal));
    }
}
