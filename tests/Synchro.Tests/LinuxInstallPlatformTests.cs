using Shouldly;
using Synchro.Core;
using Synchro.Core.Install;
using Xunit;

namespace Synchro.Tests;

public class LinuxInstallPlatformTests
{
    private static BinaryPlacement Place(InstallTestHome home, string sourcePath, string version) =>
        home.Linux().PlaceBinary(sourcePath, version).ShouldBeOfType<Result<BinaryPlacement>.Ok>().Value;

    private static NameRegistration Register(InstallTestHome home, bool pathFixRequested, bool forceName) =>
        home.Linux().RegisterName("1.0.0", pathFixRequested, forceName)
            .ShouldBeOfType<Result<NameRegistration>.Ok>().Value;

    private static string VersionedBinary(InstallTestHome home, string version) =>
        LinuxInstallLayout.BinaryPath(home.DataRoot, version);

    [Fact]
    public void PlaceBinary_NewVersion_CopiesExecutableIntoVersionedFolder()
    {
        var home = new InstallTestHome();
        try
        {
            var source = home.WriteSource("1.0.0", "binary one");

            var placement = Place(home, source, "1.0.0");

            placement.TargetPath.ShouldBe(VersionedBinary(home, "1.0.0"));
            File.ReadAllText(placement.TargetPath).ShouldBe("binary one");
            if (OperatingSystem.IsLinux())
            {
                File.GetUnixFileMode(placement.TargetPath)
                    .HasFlag(UnixFileMode.UserExecute).ShouldBeTrue();
            }
            placement.UsedLockedTargetFallback.ShouldBeFalse();
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void PlaceBinary_SameVersion_ReplacesContentWithoutFallback()
    {
        var home = new InstallTestHome();
        try
        {
            Place(home, home.WriteSource("1.0.0", "old bits"), "1.0.0");
            var rewritten = home.WriteSource("1.0.0", "new bits");

            var placement = Place(home, rewritten, "1.0.0");

            placement.UsedLockedTargetFallback.ShouldBeFalse();
            File.ReadAllText(VersionedBinary(home, "1.0.0")).ShouldBe("new bits");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void RegisterName_WhenBinDirOnPath_ReportsReadyWithoutTouchingProfile()
    {
        var home = new InstallTestHome { PathEnvironment = "/usr/bin:/opt/tools/bin" };
        var source = home.WriteSource("1.0.0", "binary one");
        Place(home, source, "1.0.0");
        home.PathEnvironment = home.BinDir;
        try
        {
            var registration = Register(home, pathFixRequested: false, forceName: false);

            registration.Registered.ShouldBeTrue();
            registration.PathReady.ShouldBeTrue();
            File.Exists(home.ProfilePath).ShouldBeFalse();
            LinuxNameLink.ResolveTarget(home.LinkPath).ShouldBe(VersionedBinary(home, "1.0.0"));
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void RegisterName_WhenNotOnPathWithoutPathFix_AdvisesManualInstruction()
    {
        var home = new InstallTestHome();
        var source = home.WriteSource("1.0.0", "binary one");
        Place(home, source, "1.0.0");
        try
        {
            var registration = Register(home, pathFixRequested: false, forceName: false);

            registration.PathReady.ShouldBeFalse();
            registration.Notes.ShouldContain(note => note.Contains("--path-fix"));
            File.Exists(home.ProfilePath).ShouldBeFalse();
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void RegisterName_WithPathFixRequested_AppendsBlockExactlyOnce()
    {
        var home = new InstallTestHome();
        var source = home.WriteSource("1.0.0", "binary one");
        Place(home, source, "1.0.0");
        try
        {
            Register(home, pathFixRequested: true, forceName: false);
            var profileAfterFirst = File.ReadAllText(home.ProfilePath);

            Register(home, pathFixRequested: true, forceName: false);

            File.ReadAllText(home.ProfilePath).ShouldBe(profileAfterFirst);
            LinuxPathFix.HasBlock(profileAfterFirst).ShouldBeTrue();
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void RegisterName_WithForeignLink_RefusesUntilForced()
    {
        var home = new InstallTestHome();
        var source = home.WriteSource("1.0.0", "binary one");
        Place(home, source, "1.0.0");
        var foreignTarget = Path.Combine(home.Home, "elsewhere");
        File.WriteAllText(foreignTarget, "other tool");
        Directory.CreateDirectory(home.BinDir);
        File.CreateSymbolicLink(home.LinkPath, foreignTarget);
        try
        {
            home.Linux().RegisterName("1.0.0", false, false)
                .ShouldBeOfType<Result<NameRegistration>.Fail>();

            var forced = Register(home, pathFixRequested: false, forceName: true);

            forced.Registered.ShouldBeTrue();
            LinuxNameLink.ResolveTarget(home.LinkPath).ShouldBe(VersionedBinary(home, "1.0.0"));
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void RemoveOtherVersions_DeletesOtherVersionFoldersOnly()
    {
        var home = new InstallTestHome();
        try
        {
            Directory.CreateDirectory(LinuxInstallLayout.VersionDir(home.DataRoot, "0.9.0"));
            File.WriteAllText(Path.Combine(home.DataRoot, "0.9.0", "synchro"), "zero");
            Directory.CreateDirectory(LinuxInstallLayout.VersionDir(home.DataRoot, "1.0.0"));
            File.WriteAllText(Path.Combine(home.DataRoot, "1.0.0", "synchro"), "one");

            var removed = home.Linux().RemoveOtherVersions("1.0.0")
                .ShouldBeOfType<Result<IReadOnlyList<string>>.Ok>().Value;

            removed.ShouldBe(["0.9.0"]);
            Directory.Exists(LinuxInstallLayout.VersionDir(home.DataRoot, "0.9.0")).ShouldBeFalse();
            Directory.Exists(LinuxInstallLayout.VersionDir(home.DataRoot, "1.0.0")).ShouldBeTrue();
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Uninstall_AfterInstall_RemovesLinkLayoutAndProfileBlock()
    {
        var home = new InstallTestHome();
        var source = home.WriteSource("1.0.0", "binary one");
        Place(home, source, "1.0.0");
        Register(home, pathFixRequested: true, forceName: false);
        try
        {
            var outcome = home.Linux().Uninstall().ShouldBeOfType<Result<UninstallOutcome>.Ok>().Value;

            outcome.NothingInstalled.ShouldBeFalse();
            outcome.BinaryRemoved.ShouldBeTrue();
            outcome.NameRemoved.ShouldBeTrue();
            outcome.PathEntryRemoved.ShouldBeTrue();
            Directory.Exists(home.DataRoot).ShouldBeFalse();
            File.Exists(home.LinkPath).ShouldBeFalse();
            LinuxPathFix.HasBlock(File.ReadAllText(home.ProfilePath)).ShouldBeFalse();
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Uninstall_WhenLinkForeign_PreservesLinkAndRemovesLayout()
    {
        var home = new InstallTestHome();
        var source = home.WriteSource("1.0.0", "binary one");
        Place(home, source, "1.0.0");
        var foreignTarget = Path.Combine(home.Home, "elsewhere");
        File.WriteAllText(foreignTarget, "other tool");
        Directory.CreateDirectory(home.BinDir);
        File.CreateSymbolicLink(home.LinkPath, foreignTarget);
        try
        {
            var outcome = home.Linux().Uninstall().ShouldBeOfType<Result<UninstallOutcome>.Ok>().Value;

            outcome.NameRemoved.ShouldBeFalse();
            outcome.BinaryRemoved.ShouldBeTrue();
            Directory.Exists(home.DataRoot).ShouldBeFalse();
            LinuxNameLink.ResolveTarget(home.LinkPath).ShouldBe(foreignTarget);
            outcome.Notes.ShouldContain(note => note.Contains("outside the synchro layout"));
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Uninstall_WhenNothingInstalled_ReportsNothing()
    {
        var home = new InstallTestHome();
        try
        {
            var outcome = home.Linux().Uninstall().ShouldBeOfType<Result<UninstallOutcome>.Ok>().Value;

            outcome.NothingInstalled.ShouldBeTrue();
            outcome.BinaryRemoved.ShouldBeFalse();
            outcome.NameRemoved.ShouldBeFalse();
            outcome.PathEntryRemoved.ShouldBeFalse();
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }
}
