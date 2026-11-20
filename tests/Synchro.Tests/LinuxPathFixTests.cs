using Shouldly;
using Synchro.Core;
using Synchro.Core.Install;
using Xunit;

namespace Synchro.Tests;

public class LinuxPathFixTests
{
    private static string BlockFor(string binDir) => LinuxPathFix.Block(binDir);

    [Fact]
    public void Append_WhenProfileMissing_CreatesFileWithBlock()
    {
        var home = new InstallTestHome();
        try
        {
            var appended = LinuxPathFix.Append(home.ProfilePath, home.BinDir)
                .ShouldBeOfType<Result<bool>.Ok>().Value;

            appended.ShouldBeTrue();
            File.ReadAllText(home.ProfilePath).ShouldBe(BlockFor(home.BinDir));
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Append_WhenBlockAlreadyPresent_IsIdempotent()
    {
        var home = new InstallTestHome();
        try
        {
            LinuxPathFix.Append(home.ProfilePath, home.BinDir).ShouldBeOfType<Result<bool>.Ok>();
            var before = File.ReadAllText(home.ProfilePath);

            var second = LinuxPathFix.Append(home.ProfilePath, home.BinDir)
                .ShouldBeOfType<Result<bool>.Ok>().Value;

            second.ShouldBeFalse();
            File.ReadAllText(home.ProfilePath).ShouldBe(before);
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Append_WhenContentLacksTrailingNewline_InsertsSeparator()
    {
        var home = new InstallTestHome();
        try
        {
            File.WriteAllText(home.ProfilePath, "alias gs='git status'");

            LinuxPathFix.Append(home.ProfilePath, home.BinDir).ShouldBeOfType<Result<bool>.Ok>();

            File.ReadAllText(home.ProfilePath)
                .ShouldBe("alias gs='git status'\n" + BlockFor(home.BinDir));
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Remove_WhenBlockPresent_RemovesExactlyTheBlock()
    {
        var home = new InstallTestHome();
        try
        {
            File.WriteAllText(home.ProfilePath, "line one\nline two\n" + BlockFor(home.BinDir) + "line three\n");

            var removed = LinuxPathFix.Remove(home.ProfilePath)
                .ShouldBeOfType<Result<bool>.Ok>().Value;

            removed.ShouldBeTrue();
            File.ReadAllText(home.ProfilePath).ShouldBe("line one\nline two\nline three\n");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Remove_WhenBlockIsWholeFile_LeavesEmptyFile()
    {
        var home = new InstallTestHome();
        try
        {
            File.WriteAllText(home.ProfilePath, BlockFor(home.BinDir));

            LinuxPathFix.Remove(home.ProfilePath).ShouldBeOfType<Result<bool>.Ok>();

            File.ReadAllText(home.ProfilePath).ShouldBe(string.Empty);
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Remove_WhenBlockAbsent_LeavesFileUnchanged()
    {
        var home = new InstallTestHome();
        try
        {
            File.WriteAllText(home.ProfilePath, "line one\n");

            var removed = LinuxPathFix.Remove(home.ProfilePath)
                .ShouldBeOfType<Result<bool>.Ok>().Value;

            removed.ShouldBeFalse();
            File.ReadAllText(home.ProfilePath).ShouldBe("line one\n");
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }

    [Fact]
    public void Remove_WhenProfileMissing_ReturnsFalse()
    {
        var home = new InstallTestHome();
        try
        {
            LinuxPathFix.Remove(home.ProfilePath)
                .ShouldBeOfType<Result<bool>.Ok>().Value.ShouldBeFalse();
            File.Exists(home.ProfilePath).ShouldBeFalse();
        }
        finally
        {
            InstallTestHome.Delete(home);
        }
    }
}
