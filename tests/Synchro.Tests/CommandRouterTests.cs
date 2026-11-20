using Shouldly;
using Synchro.App;
using Xunit;

namespace Synchro.Tests;

public class CommandRouterTests
{
    [Theory]
    [InlineData("install")]
    [InlineData("uninstall")]
    public void Run_InstallCommandsHelp_IsRoutedAndExitsZero(string command)
    {
        CommandRouter.Run([command, "--help"]).ShouldBe(0);
    }

    [Fact]
    public void Run_InstallParseError_ExitsBeforeAnyDiscovery()
    {
        CommandRouter.Run(["install", "stray"]).ShouldBe(2);
    }

    [Fact]
    public void Run_UninstallParseError_ExitsWithParseCode()
    {
        CommandRouter.Run(["uninstall", "--purge"]).ShouldBe(2);
    }
}
