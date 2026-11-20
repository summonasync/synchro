using Synchro.Core.Install;

namespace Synchro.Tests;

public sealed class InstallTestHome
{
    public string Home { get; }

    public string XdgDataHome { get; }

    public string LocalAppData { get; }

    public string PathEnvironment { get; set; } = string.Empty;

    public InstallTestHome()
    {
        Home = TempDir.Create();
        XdgDataHome = Path.Combine(Home, "xdg-data");
        LocalAppData = Path.Combine(Home, "appdata-local");
        Directory.CreateDirectory(XdgDataHome);
        Directory.CreateDirectory(LocalAppData);
    }

    public string ProfilePath => Path.Combine(Home, ".profile");

    public string BinDir => Path.Combine(Home, ".local", "bin");

    public string LinkPath => Path.Combine(BinDir, "synchro");

    public string DataRoot => Path.Combine(XdgDataHome, "synchro");

    public string WindowsRoot => Path.Combine(LocalAppData, "Programs", "synchro");

    public string WriteSource(string version, string content)
    {
        var path = Path.Combine(Home, $"incoming-{version}");
        File.WriteAllText(path, content);
        return path;
    }

    public LinuxInstallPlatform Linux() => new(Home, XdgDataHome, PathEnvironment);

    public WindowsInstallPlatform Windows(IUserPathStore pathStore) => new(LocalAppData, pathStore);

    public static void Delete(InstallTestHome home) => Directory.Delete(home.Home, true);
}
