using Shouldly;
using Synchro.App.Commands;
using Synchro.Core;
using Synchro.Core.Install;
using Xunit;

namespace Synchro.Tests;

public class UninstallCommandTests
{
    private static readonly UninstallArgs Args = new() { WantsHelp = false };

    private static (int ExitCode, string Output, string Error) Run(IInstallPlatform platform)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = UninstallCommand.Run(Args, platform, output, error);

        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public void Run_AfterInstall_RemovesLayoutProfileAndLink()
    {
        var home = new InstallTestHome();
        try
        {
            Install(home);

            var result = Run(home.Linux());

            result.ExitCode.ShouldBe(0);
            result.Output.ShouldContain("uninstalled: synchro 0.2.0");
            result.Output.ShouldContain("binary removed");
            result.Output.ShouldContain("command name removed");
            result.Output.ShouldContain("PATH entry removed");
            File.Exists(home.LinkPath).ShouldBeFalse();
            Directory.Exists(home.DataRoot).ShouldBeFalse();
            File.ReadAllText(home.ProfilePath).ShouldNotContain(LinuxPathFix.StartMarker);
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Run_NothingInstalled_ReportsNothing()
    {
        var home = new InstallTestHome();
        try
        {
            var result = Run(home.Linux());

            result.ExitCode.ShouldBe(0);
            result.Output.ShouldContain("nothing installed — nothing to remove");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Run_KeepsForeignNameLink()
    {
        var home = new InstallTestHome();
        try
        {
            Directory.CreateDirectory(home.BinDir);
            var foreign = Path.Combine(home.Home, "other-tool", "synchro");
            Directory.CreateDirectory(Path.GetDirectoryName(foreign)!);
            File.WriteAllText(foreign, "another tool");
            File.CreateSymbolicLink(home.LinkPath, foreign);

            var result = Run(home.Linux());

            result.Output.ShouldContain($"left '{home.LinkPath}' in place");
            File.Exists(home.LinkPath).ShouldBeTrue();
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Run_WindowsPlatform_RemovesPathEntryAndWarnsAboutTerminals()
    {
        var home = new InstallTestHome();
        try
        {
            Install(home, home.Windows(new FakeUserPathStore()));

            var pathStore = new FakeUserPathStore { Value = $"{home.WindowsRoot};C:\\Tools" };
            var result = Run(home.Windows(pathStore));

            result.ExitCode.ShouldBe(0);
            result.Output.ShouldContain("uninstalled: synchro");
            result.Output.ShouldContain("binary removed");
            result.Output.ShouldContain("already open terminals keep resolving synchro until restarted");
            pathStore.Value.ShouldBe("C:\\Tools");
            File.Exists(Path.Combine(home.WindowsRoot, "synchro.exe")).ShouldBeFalse();
            result.Output.ShouldContain("installation folder could not be fully removed");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    private static void Install(InstallTestHome home) =>
        Install(home, home.Linux());

    private static void Install(InstallTestHome home, IInstallPlatform platform)
    {
        var source = home.WriteSource("0.2.0", "synchro bits 0.2.0");
        var engine = new InstallEngine(platform);
        var report = engine.Install(new InstallRequest
        {
            SourcePath = source,
            Version = "0.2.0",
            ApplyPathFix = true
        });

        report.ShouldBeOfType<Result<InstallReport>.Ok>();
    }
}
