using Shouldly;
using Synchro.App.Commands;
using Synchro.Core.Install;
using Xunit;

namespace Synchro.Tests;

public class InstallCommandTests
{
    private static InstallArgs Args(bool pathFix = false, bool force = false) =>
        new() { PathFix = pathFix, Force = force, WantsHelp = false };

    private static InstallRuntime Runtime(InstallTestHome home, string version, IInstallPlatform? platform = null)
    {
        var source = home.WriteSource(version, $"synchro bits {version}");
        return new InstallRuntime
        {
            SourcePath = source,
            Version = version,
            Platform = platform ?? home.Linux(),
            NameLinkPath = platform is null ? home.LinkPath : null
        };
    }

    private static (int ExitCode, string Output, string Error) Run(InstallArgs args, InstallRuntime runtime)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = InstallCommand.Run(args, runtime, output, error);

        return (exitCode, output.ToString(), error.ToString());
    }

    private static string InstalledBinary(InstallTestHome home, string version) =>
        Path.Combine(home.DataRoot, version, "synchro");

    [Fact]
    public void Run_FreshInstall_WritesBinaryLinkAndReport()
    {
        var home = new InstallTestHome();
        home.PathEnvironment = home.BinDir;
        try
        {
            var result = Run(Args(), Runtime(home, "0.2.0"));

            result.ExitCode.ShouldBe(0);
            result.Error.ShouldBeEmpty();
            result.Output.ShouldContain($"installed: synchro 0.2.0 -> {InstalledBinary(home, "0.2.0")}");
            File.Exists(InstalledBinary(home, "0.2.0")).ShouldBeTrue();
            LinuxNameLink.ResolveTarget(home.LinkPath).ShouldBe(InstalledBinary(home, "0.2.0"));
            result.Output.ShouldNotContain("upgrade:");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Run_Upgrade_ReportsVersionTransitionAndPrunedFolders()
    {
        var home = new InstallTestHome();
        home.PathEnvironment = home.BinDir;
        try
        {
            Run(Args(), Runtime(home, "0.1.0"));

            var result = Run(Args(), Runtime(home, "0.2.0"));

            result.ExitCode.ShouldBe(0);
            result.Output.ShouldContain("upgrade: 0.1.0 -> 0.2.0");
            result.Output.ShouldContain("removed previous version folders: 0.1.0");
            Directory.Exists(Path.Combine(home.DataRoot, "0.1.0")).ShouldBeFalse();
            LinuxNameLink.ResolveTarget(home.LinkPath).ShouldBe(InstalledBinary(home, "0.2.0"));
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Run_ReInstallFromInstalledCopy_ReportsUpToDate()
    {
        var home = new InstallTestHome();
        home.PathEnvironment = home.BinDir;
        try
        {
            Run(Args(), Runtime(home, "0.2.0"));

            var runtime = Runtime(home, "0.2.0");
            var result = Run(Args(), runtime with { SourcePath = InstalledBinary(home, "0.2.0") });

            result.ExitCode.ShouldBe(0);
            result.Output.ShouldContain("synchro 0.2.0 is already installed — nothing to do");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Run_MissingPathEntryWithoutFix_HintsRerunWithPathFix()
    {
        var home = new InstallTestHome();
        try
        {
            var result = Run(Args(), Runtime(home, "0.2.0"));

            result.ExitCode.ShouldBe(0);
            result.Output.ShouldContain("is not on PATH");
            result.Output.ShouldContain("--path-fix");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Run_PathFix_AppendsProfileBlockAndHintsNewShell()
    {
        var home = new InstallTestHome();
        try
        {
            var result = Run(Args(pathFix: true), Runtime(home, "0.2.0"));

            result.ExitCode.ShouldBe(0);
            result.Output.ShouldContain("profile block appended — open a new shell to pick up the PATH");
            File.ReadAllText(home.ProfilePath).ShouldContain(LinuxPathFix.StartMarker);
            File.ReadAllText(home.ProfilePath).ShouldContain(home.BinDir);
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Run_ForeignNameLinkWithoutForce_Refuses()
    {
        var home = new InstallTestHome();
        try
        {
            Directory.CreateDirectory(home.BinDir);
            var foreign = Path.Combine(home.Home, "other-tool", "synchro");
            Directory.CreateDirectory(Path.GetDirectoryName(foreign)!);
            File.WriteAllText(foreign, "another tool");
            File.CreateSymbolicLink(home.LinkPath, foreign);

            var result = Run(Args(), Runtime(home, "0.2.0"));

            result.ExitCode.ShouldBe(1);
            result.Error.ShouldContain("rerun with --force");
            LinuxNameLink.ResolveTarget(home.LinkPath).ShouldBe(foreign);
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Run_ForeignNameLinkWithForce_WarnsAndRePoints()
    {
        var home = new InstallTestHome();
        home.PathEnvironment = home.BinDir;
        try
        {
            Directory.CreateDirectory(home.BinDir);
            var foreign = Path.Combine(home.Home, "other-tool", "synchro");
            Directory.CreateDirectory(Path.GetDirectoryName(foreign)!);
            File.WriteAllText(foreign, "another tool");
            File.CreateSymbolicLink(home.LinkPath, foreign);

            var result = Run(Args(force: true), Runtime(home, "0.2.0"));

            result.ExitCode.ShouldBe(0);
            result.Output.ShouldContain($"warning: re-pointing '{home.LinkPath}'");
            result.Output.ShouldContain(foreign);
            LinuxNameLink.ResolveTarget(home.LinkPath).ShouldBe(InstalledBinary(home, "0.2.0"));
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Run_ForceOnFreshName_WarnsNothing()
    {
        var home = new InstallTestHome();
        home.PathEnvironment = home.BinDir;
        try
        {
            var result = Run(Args(force: true), Runtime(home, "0.2.0"));

            result.ExitCode.ShouldBe(0);
            result.Output.ShouldNotContain("warning:");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Run_WindowsPlatform_RegistersPathAndHintsNewTerminal()
    {
        var home = new InstallTestHome();
        var pathStore = new FakeUserPathStore();
        try
        {
            var result = Run(Args(), Runtime(home, "0.2.0", home.Windows(pathStore)));

            result.ExitCode.ShouldBe(0);
            result.Output.ShouldContain($"installed: synchro 0.2.0 -> {Path.Combine(home.WindowsRoot, "synchro.exe")}");
            result.Output.ShouldContain("PATH updated — open a new terminal to pick it up");
            pathStore.Value.ShouldNotBeNull();
            pathStore.Value.ShouldContain(home.WindowsRoot);
            File.Exists(Path.Combine(home.WindowsRoot, "synchro.exe")).ShouldBeTrue();
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Run_WindowsPlatformWithPathFix_ExplainsPathFixIsLinuxOnly()
    {
        var home = new InstallTestHome();
        try
        {
            var result = Run(Args(pathFix: true), Runtime(home, "0.2.0", home.Windows(new FakeUserPathStore())));

            result.Output.ShouldContain("--path-fix applies to linux profiles");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Run_MissingSourceBinary_Fails()
    {
        var home = new InstallTestHome();
        home.PathEnvironment = home.BinDir;
        try
        {
            var runtime = Runtime(home, "0.2.0");
            File.Delete(runtime.SourcePath);

            var result = Run(Args(), runtime);

            result.ExitCode.ShouldBe(1);
            result.Error.ShouldContain("install failed:");
            result.Error.ShouldContain("source binary not found");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }
}
