using Shouldly;
using Synchro.Core;
using Synchro.Core.Install;
using Xunit;

namespace Synchro.Tests;

public class LinuxNameLinkTests
{
    private static LinkSwapped Swap(InstallTestHome home, string targetPath, bool force) =>
        LinuxNameLink.Swap(home.LinkPath, targetPath, home.DataRoot, force)
            .ShouldBeOfType<Result<LinkSwapped>.Ok>().Value;

    private static string SwapFails(InstallTestHome home, string targetPath, bool force) =>
        LinuxNameLink.Swap(home.LinkPath, targetPath, home.DataRoot, force)
            .ShouldBeOfType<Result<LinkSwapped>.Fail>().Error.Message;

    private static string WriteVersionedBinary(InstallTestHome home, string version, string content)
    {
        var path = LinuxInstallLayout.BinaryPath(home.DataRoot, version);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Swap_WhenLinkMissing_CreatesLink()
    {
        var home = new InstallTestHome();
        try
        {
            var target = WriteVersionedBinary(home, "1.0.0", "one");

            var swap = Swap(home, target, force: false);

            swap.Created.ShouldBeTrue();
            swap.RePointed.ShouldBeFalse();
            LinuxNameLink.ResolveTarget(home.LinkPath).ShouldBe(target);
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Swap_WhenLinkOwnedByLayout_RePointsAtomically()
    {
        var home = new InstallTestHome();
        try
        {
            var oldTarget = WriteVersionedBinary(home, "1.0.0", "one");
            var newTarget = WriteVersionedBinary(home, "2.0.0", "two");
            Swap(home, oldTarget, force: false);

            var swap = Swap(home, newTarget, force: false);

            swap.Created.ShouldBeFalse();
            swap.RePointed.ShouldBeTrue();
            swap.ForcedForeign.ShouldBeFalse();
            LinuxNameLink.ResolveTarget(home.LinkPath).ShouldBe(newTarget);
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Swap_WhenLinkPointsOutsideLayout_RefusesWithoutForce()
    {
        var home = new InstallTestHome();
        try
        {
            var foreignTarget = Path.Combine(home.Home, "elsewhere");
            File.WriteAllText(foreignTarget, "other tool");
            Directory.CreateDirectory(home.BinDir);
            File.CreateSymbolicLink(home.LinkPath, foreignTarget);
            var ownedTarget = WriteVersionedBinary(home, "1.0.0", "one");

            var failure = SwapFails(home, ownedTarget, force: false);

            failure.ShouldContain("elsewhere");
            failure.ShouldContain("--force");
            LinuxNameLink.ResolveTarget(home.LinkPath).ShouldBe(foreignTarget);
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Swap_WhenForeignLinkAndForce_RePointsAndNamesPreviousTarget()
    {
        var home = new InstallTestHome();
        try
        {
            var foreignTarget = Path.Combine(home.Home, "elsewhere");
            File.WriteAllText(foreignTarget, "other tool");
            Directory.CreateDirectory(home.BinDir);
            File.CreateSymbolicLink(home.LinkPath, foreignTarget);
            var ownedTarget = WriteVersionedBinary(home, "1.0.0", "one");

            var swap = Swap(home, ownedTarget, force: true);

            swap.RePointed.ShouldBeTrue();
            swap.ForcedForeign.ShouldBeTrue();
            swap.ForeignTarget.ShouldBe(foreignTarget);
            LinuxNameLink.ResolveTarget(home.LinkPath).ShouldBe(ownedTarget);
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Swap_WhenPathIsRegularFile_TreatsItAsForeign()
    {
        var home = new InstallTestHome();
        try
        {
            Directory.CreateDirectory(home.BinDir);
            File.WriteAllText(home.LinkPath, "not a symlink");
            var ownedTarget = WriteVersionedBinary(home, "1.0.0", "one");

            SwapFails(home, ownedTarget, force: false).ShouldContain("--force");
            File.ReadAllText(home.LinkPath).ShouldBe("not a symlink");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Swap_WhenOwnedLinkDangling_RePointsToNewTarget()
    {
        var home = new InstallTestHome();
        try
        {
            var retiredTarget = WriteVersionedBinary(home, "0.9.0", "zero");
            Directory.CreateDirectory(home.BinDir);
            File.CreateSymbolicLink(home.LinkPath, retiredTarget);
            File.Delete(retiredTarget);
            var newTarget = WriteVersionedBinary(home, "1.0.0", "one");

            var swap = Swap(home, newTarget, force: false);

            swap.RePointed.ShouldBeTrue();
            LinuxNameLink.ResolveTarget(home.LinkPath).ShouldBe(newTarget);
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }
}
