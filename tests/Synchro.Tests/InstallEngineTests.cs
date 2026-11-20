using Shouldly;
using Synchro.Core;
using Synchro.Core.Install;
using Xunit;

namespace Synchro.Tests;

public class InstallEngineTests
{
    private static InstallEngine Engine(InstallTestHome home) => new(home.Linux());

    private static InstallReport Install(InstallEngine engine, InstallRequest request) =>
        engine.Install(request).ShouldBeOfType<Result<InstallReport>.Ok>().Value;

    private static UninstallReport Uninstall(InstallEngine engine) =>
        engine.Uninstall().ShouldBeOfType<Result<UninstallReport>.Ok>().Value;

    private static InstallRequest Request(InstallTestHome home, string version, string content, bool pathFix = false) =>
        new() { SourcePath = home.WriteSource(version, content), Version = version, ApplyPathFix = pathFix };

    [Fact]
    public void Install_Fresh_CopiesRegistersAndReports()
    {
        var home = new InstallTestHome { PathEnvironment = "/usr/bin" };
        try
        {
            home.PathEnvironment = home.BinDir;
            var engine = Engine(home);

            var report = Install(engine, Request(home, "1.0.0", "bits one"));

            report.UpToDate.ShouldBeFalse();
            report.PreviousVersion.ShouldBeNull();
            report.Version.ShouldBe("1.0.0");
            report.BinaryPath.ShouldBe(LinuxInstallLayout.BinaryPath(home.DataRoot, "1.0.0"));
            report.NameRegistered.ShouldBeTrue();
            report.PathReady.ShouldBeTrue();
            report.UsedLockedTargetFallback.ShouldBeFalse();
            LinuxNameLink.ResolveTarget(home.LinkPath).ShouldBe(report.BinaryPath);
            InstallManifest.Read(home.DataRoot).ShouldBeOfType<Result<string?>.Ok>().Value.ShouldBe("1.0.0");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Install_Upgrade_ReportsPreviousVersionAndPrunesOldFolders()
    {
        var home = new InstallTestHome();
        home.PathEnvironment = home.BinDir;
        try
        {
            var engine = Engine(home);
            Install(engine, Request(home, "1.0.0", "bits one"));

            var report = Install(engine, Request(home, "2.0.0", "bits two"));

            report.PreviousVersion.ShouldBe("1.0.0");
            report.Version.ShouldBe("2.0.0");
            LinuxNameLink.ResolveTarget(home.LinkPath).ShouldBe(LinuxInstallLayout.BinaryPath(home.DataRoot, "2.0.0"));
            File.ReadAllText(LinuxInstallLayout.BinaryPath(home.DataRoot, "2.0.0")).ShouldBe("bits two");
            Directory.Exists(LinuxInstallLayout.VersionDir(home.DataRoot, "1.0.0")).ShouldBeFalse();
            InstallManifest.Read(home.DataRoot).ShouldBeOfType<Result<string?>.Ok>().Value.ShouldBe("2.0.0");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Install_SameVersionFromInstalledBinary_IsUpToDateWithoutWrites()
    {
        var home = new InstallTestHome();
        home.PathEnvironment = home.BinDir;
        try
        {
            var engine = Engine(home);
            var first = Install(engine, Request(home, "1.0.0", "bits one"));
            var before = File.ReadAllText(first.BinaryPath);

            var report = Install(engine, new InstallRequest { SourcePath = first.BinaryPath, Version = "1.0.0" });

            report.UpToDate.ShouldBeTrue();
            report.PreviousVersion.ShouldBe("1.0.0");
            report.Notes.ShouldContain(note => note.Contains("already installed"));
            File.ReadAllText(first.BinaryPath).ShouldBe(before);
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Install_WhenTargetCannotBeReplaced_UsesLockedTargetFallback()
    {
        var home = new InstallTestHome();
        home.PathEnvironment = home.BinDir;
        try
        {
            var engine = Engine(home);
            Install(engine, Request(home, "1.0.0", "bits one"));
            var target = LinuxInstallLayout.BinaryPath(home.DataRoot, "1.0.0");
            File.Delete(target);
            Directory.CreateDirectory(target);

            var report = Install(engine, new InstallRequest
            {
                SourcePath = home.WriteSource("1.0.0", "bits one rewritten"),
                Version = "1.0.0"
            });

            report.UsedLockedTargetFallback.ShouldBeTrue();
            Directory.Exists(target).ShouldBeFalse();
            File.ReadAllText(target).ShouldBe("bits one rewritten");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Install_WithMissingSource_Fails()
    {
        var home = new InstallTestHome();
        try
        {
            var engine = Engine(home);

            var install = engine.Install(new InstallRequest
            {
                SourcePath = Path.Combine(home.Home, "ghost"),
                Version = "1.0.0"
            });

            install.ShouldBeOfType<Result<InstallReport>.Fail>()
                .Error.Code.ShouldBe("not_found");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("..")]
    [InlineData("1.0/2.0")]
    [InlineData(@"1.0\2.0")]
    public void Install_WithNonPlainVersionLabel_Fails(string version)
    {
        var home = new InstallTestHome();
        try
        {
            var engine = Engine(home);

            var install = engine.Install(new InstallRequest
            {
                SourcePath = home.WriteSource("src", "bits"),
                Version = version
            });

            install.ShouldBeOfType<Result<InstallReport>.Fail>()
                .Error.Code.ShouldBe("invalid");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Install_WithCorruptManifest_TreatsRunAsFreshInstall()
    {
        var home = new InstallTestHome();
        home.PathEnvironment = home.BinDir;
        try
        {
            Directory.CreateDirectory(home.DataRoot);
            File.WriteAllText(InstallManifest.PathFor(home.DataRoot), "broken {");
            var engine = Engine(home);

            var report = Install(engine, Request(home, "1.0.0", "bits one"));

            report.PreviousVersion.ShouldBeNull();
            report.Notes.ShouldContain(note => note.Contains("unreadable"));
            InstallManifest.Read(home.DataRoot).ShouldBeOfType<Result<string?>.Ok>().Value.ShouldBe("1.0.0");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Uninstall_AfterInstallWithExplicitHomeWithoutPathEntry_IsSymmetric()
    {
        var home = new InstallTestHome();
        var engine = Engine(home);
        Install(engine, Request(home, "1.0.0", "bits one", pathFix: true));
        try
        {
            var report = Uninstall(engine);

            report.NothingInstalled.ShouldBeFalse();
            report.RemovedVersion.ShouldBe("1.0.0");
            report.BinaryRemoved.ShouldBeTrue();
            report.NameRemoved.ShouldBeTrue();
            report.PathEntryRemoved.ShouldBeTrue();
            Directory.Exists(home.DataRoot).ShouldBeFalse();
            File.Exists(home.LinkPath).ShouldBeFalse();
            LinuxPathFix.HasBlock(File.ReadAllText(home.ProfilePath)).ShouldBeFalse();
            engine.Uninstall().ShouldBeOfType<Result<UninstallReport>.Ok>()
                .Value.NothingInstalled.ShouldBeTrue();
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
            var report = Uninstall(Engine(home));

            report.NothingInstalled.ShouldBeTrue();
            report.RemovedVersion.ShouldBeNull();
            report.BinaryRemoved.ShouldBeFalse();
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Uninstall_WithCorruptManifest_StillRemovesLayout()
    {
        var home = new InstallTestHome();
        home.PathEnvironment = home.BinDir;
        try
        {
            var engine = Engine(home);
            Install(engine, Request(home, "1.0.0", "bits one"));
            File.WriteAllText(InstallManifest.PathFor(home.DataRoot), "broken {");

            var report = Uninstall(engine);

            report.RemovedVersion.ShouldBeNull();
            report.Notes.ShouldContain(note => note.Contains("unreadable"));
            report.BinaryRemoved.ShouldBeTrue();
            Directory.Exists(home.DataRoot).ShouldBeFalse();
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Install_OnWindowsPlatform_UpgradesInPlaceWithoutDuplicatePathEntry()
    {
        var home = new InstallTestHome();
        try
        {
            var store = new FakeUserPathStore { Value = @"C:\tools;%JAVA_HOME%\bin" };
            var engine = new InstallEngine(home.Windows(store));
            Install(engine, new InstallRequest { SourcePath = Source(home, "windows one"), Version = "1.0.0" });

            var report = Install(engine, new InstallRequest { SourcePath = Source(home, "windows two"), Version = "2.0.0" });

            report.PreviousVersion.ShouldBe("1.0.0");
            report.Version.ShouldBe("2.0.0");
            File.ReadAllText(Path.Combine(home.WindowsRoot, "synchro.exe")).ShouldBe("windows two");
            store.Value.ShouldBe(@"C:\tools;%JAVA_HOME%\bin;" + home.WindowsRoot);
            store.WriteCount.ShouldBe(1);
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    private static string Source(InstallTestHome home, string content)
    {
        var path = Path.Combine(home.Home, "incoming-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, content);
        return path;
    }
}
