namespace Synchro.Core.Install;

public static class WindowsPathValueKind
{
    public static bool ProbeFailureDefaultsToExpandString(Exception ex) =>
        ex is IOException or System.Security.SecurityException or ArgumentException;
}
