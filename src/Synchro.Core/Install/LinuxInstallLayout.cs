namespace Synchro.Core.Install;

public static class LinuxInstallLayout
{
    public const string BinaryName = "synchro";

    public static string DataRoot(string homeDir, string? xdgDataHome) =>
        Path.Combine(xdgDataHome ?? Path.Combine(homeDir, ".local", "share"), "synchro");

    public static string VersionDir(string root, string version) => Path.Combine(root, version);

    public static string BinaryPath(string root, string version) => Path.Combine(VersionDir(root, version), BinaryName);

    public static string BinDir(string homeDir) => Path.Combine(homeDir, ".local", "bin");

    public static string LinkPath(string homeDir) => Path.Combine(BinDir(homeDir), BinaryName);

    public static string ProfilePath(string homeDir) => Path.Combine(homeDir, ".profile");
}
