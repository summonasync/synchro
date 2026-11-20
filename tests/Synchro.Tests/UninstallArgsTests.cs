using Shouldly;
using Synchro.App.Commands;
using Synchro.Core;
using Xunit;

namespace Synchro.Tests;

public class UninstallArgsTests
{
    private static UninstallArgs Parse(params string[] args) =>
        UninstallArgs.Parse(args).ShouldBeOfType<Result<UninstallArgs>.Ok>().Value;

    private static string ParseFails(params string[] args) =>
        UninstallArgs.Parse(args).ShouldBeOfType<Result<UninstallArgs>.Fail>().Error.Message;

    [Fact]
    public void Parse_NoArgs_ResolvesDefaults()
    {
        Parse().WantsHelp.ShouldBeFalse();
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Parse_HelpFlag_IsRecognized(string flag)
    {
        Parse(flag).WantsHelp.ShouldBeTrue();
    }

    [Fact]
    public void Parse_UnknownOption_FailsWithUsage()
    {
        ParseFails("--purge").ShouldContain("unknown option '--purge'");
        ParseFails("--purge").ShouldContain("usage: synchro uninstall");
    }

    [Fact]
    public void Parse_PositionalArgument_FailsWithUsage()
    {
        ParseFails("extra").ShouldContain("unexpected argument 'extra'");
    }
}
