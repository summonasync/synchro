namespace Synchro.Core.Install;

public static class WindowsInstallLayout
{
    public const string ExecutableName = "synchro.exe";

    public static string ProgramDir(string localAppData) => Path.Combine(localAppData, "Programs", "synchro");

    public static string ExecutableIn(string programDir) => Path.Combine(programDir, ExecutableName);
}
