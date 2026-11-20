using Shouldly;
using Synchro.Core;
using Synchro.Core.Install;
using Xunit;

namespace Synchro.Tests;

public class WindowsInstallPlatformTests
{
    private static FakeUserPathStore Store() => new();

    private static BinaryPlacement Place(WindowsInstallPlatform platform, string sourcePath) =>
        platform.PlaceBinary(sourcePath, "1.0.0").ShouldBeOfType<Result<BinaryPlacement>.Ok>().Value;

    private static NameRegistration Register(WindowsInstallPlatform platform, bool pathFixRequested) =>
        platform.RegisterName("1.0.0", pathFixRequested, forceName: false)
            .ShouldBeOfType<Result<NameRegistration>.Ok>().Value;

    private static string Source(InstallTestHome home, string content)
    {
        var path = Path.Combine(home.Home, "incoming-windows");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void PlaceBinary_PlacesExecutableAtProgramDirectory()
    {
        var home = new InstallTestHome();
        try
        {
            var platform = home.Windows(Store());

            var placement = Place(platform, Source(home, "windows bits"));

            placement.TargetPath.ShouldBe(Path.Combine(home.WindowsRoot, "synchro.exe"));
            File.ReadAllText(placement.TargetPath).ShouldBe("windows bits");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void RegisterName_WhenEntryAdded_ReportsReadyAndNewTerminalNote()
    {
        var home = new InstallTestHome();
        try
        {
            var store = Store();
            var platform = home.Windows(store);

            var registration = Register(platform, pathFixRequested: false);

            registration.Registered.ShouldBeTrue();
            registration.PathReady.ShouldBeTrue();
            store.Value.ShouldBe(home.WindowsRoot);
            registration.Notes.ShouldContain(note => note.Contains("new terminal"));
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void RegisterName_WhenEntryAlreadyPresent_DoesNotDuplicate()
    {
        var home = new InstallTestHome();
        try
        {
            var store = new FakeUserPathStore { Value = home.WindowsRoot };
            var platform = home.Windows(store);

            var registration = Register(platform, pathFixRequested: false);

            registration.PathReady.ShouldBeTrue();
            store.Value.ShouldBe(home.WindowsRoot);
            store.WriteCount.ShouldBe(0);
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void RegisterName_WithPathFixRequest_NotesItIsALinuxOption()
    {
        var home = new InstallTestHome();
        try
        {
            var store = Store();
            var platform = home.Windows(store);

            var registration = Register(platform, pathFixRequested: true);

            store.Value.ShouldBe(home.WindowsRoot);
            registration.Notes.ShouldContain(note => note.Contains("linux"));
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void RemoveOtherVersions_IsNoOp()
    {
        var home = new InstallTestHome();
        try
        {
            home.Windows(Store()).RemoveOtherVersions("1.0.0")
                .ShouldBeOfType<Result<IReadOnlyList<string>>.Ok>().Value.ShouldBe([]);
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Uninstall_AfterInstall_RemovesExecutableAndPathEntry()
    {
        var home = new InstallTestHome();
        try
        {
            var store = Store();
            var platform = home.Windows(store);
            Place(platform, Source(home, "windows bits"));
            Register(platform, pathFixRequested: false);

            var outcome = platform.Uninstall().ShouldBeOfType<Result<UninstallOutcome>.Ok>().Value;

            outcome.BinaryRemoved.ShouldBeTrue();
            outcome.NameRemoved.ShouldBeTrue();
            outcome.PathEntryRemoved.ShouldBeTrue();
            File.Exists(Path.Combine(home.WindowsRoot, "synchro.exe")).ShouldBeFalse();
            Directory.Exists(home.WindowsRoot).ShouldBeFalse();
            store.Value.ShouldBe(string.Empty);
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
            var outcome = home.Windows(Store()).Uninstall()
                .ShouldBeOfType<Result<UninstallOutcome>.Ok>().Value;

            outcome.NothingInstalled.ShouldBeTrue();
            outcome.BinaryRemoved.ShouldBeFalse();
            outcome.NameRemoved.ShouldBeFalse();
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }
}
