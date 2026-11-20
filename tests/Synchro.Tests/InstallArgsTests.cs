using Shouldly;
using Synchro.App.Commands;
using Synchro.Core;
using Xunit;

namespace Synchro.Tests;

public class InstallArgsTests
{
    private static InstallArgs Parse(params string[] args) =>
        InstallArgs.Parse(args).ShouldBeOfType<Result<InstallArgs>.Ok>().Value;

    private static string ParseFails(params string[] args) =>
        InstallArgs.Parse(args).ShouldBeOfType<Result<InstallArgs>.Fail>().Error.Message;

    [Fact]
    public void Parse_NoArgs_ResolvesDefaults()
    {
        var parsed = Parse();

        parsed.PathFix.ShouldBeFalse();
        parsed.Force.ShouldBeFalse();
        parsed.WantsHelp.ShouldBeFalse();
    }

    [Fact]
    public void Parse_PathFixFlag_SetsPathFix()
    {
        Parse("--path-fix").PathFix.ShouldBeTrue();
    }

    [Fact]
    public void Parse_ForceFlag_SetsForce()
    {
        Parse("--force").Force.ShouldBeTrue();
    }

    [Fact]
    public void Parse_BothFlags_CarryBoth()
    {
        var parsed = Parse("--path-fix", "--force");

        parsed.PathFix.ShouldBeTrue();
        parsed.Force.ShouldBeTrue();
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Parse_HelpFlag_DoesNotTouchOtherFlags(string flag)
    {
        var parsed = Parse(flag);

        parsed.WantsHelp.ShouldBeTrue();
        parsed.PathFix.ShouldBeFalse();
        parsed.Force.ShouldBeFalse();
    }

    [Fact]
    public void Parse_UnknownOption_FailsWithUsage()
    {
        ParseFails("--verbose").ShouldContain("unknown option '--verbose'");
        ParseFails("--verbose").ShouldContain("usage: synchro install");
    }

    [Fact]
    public void Parse_PositionalArgument_FailsWithUsage()
    {
        ParseFails("somewhere").ShouldContain("unexpected argument 'somewhere'");
        ParseFails("somewhere").ShouldContain("usage: synchro install");
    }
}
