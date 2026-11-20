using Shouldly;
using Synchro.App.Commands;
using Synchro.Core;
using Xunit;

namespace Synchro.Tests;

public class RollbackArgsTests
{
    private static RollbackArgs Parse(params string[] args) =>
        RollbackArgs.Parse(args).ShouldBeOfType<Result<RollbackArgs>.Ok>().Value;

    private static string ParseFails(params string[] args) =>
        RollbackArgs.Parse(args).ShouldBeOfType<Result<RollbackArgs>.Fail>().Error.Message;

    [Fact]
    public void Parse_NoArgs_ResolvesDefaults()
    {
        var parsed = Parse();

        parsed.SlaveName.ShouldBeNull();
        parsed.PreferMaster.ShouldBeFalse();
        parsed.WantsHelp.ShouldBeFalse();
    }

    [Fact]
    public void Parse_SlaveName_CarriesPositional()
    {
        Parse("laptop").SlaveName.ShouldBe("laptop");
    }

    [Fact]
    public void Parse_MasterFlag_SetsPreferMaster()
    {
        var parsed = Parse("laptop", "--master");

        parsed.SlaveName.ShouldBe("laptop");
        parsed.PreferMaster.ShouldBeTrue();
    }

    [Fact]
    public void Parse_HelpFlag_DoesNotResolveSlave()
    {
        var parsed = Parse("--help");

        parsed.WantsHelp.ShouldBeTrue();
        parsed.SlaveName.ShouldBeNull();
    }

    [Fact]
    public void Parse_ShortHelpFlag_IsRecognized()
    {
        Parse("-h").WantsHelp.ShouldBeTrue();
    }

    [Fact]
    public void Parse_UnknownOption_FailsWithUsage()
    {
        ParseFails("--force").ShouldContain("unknown option '--force'");
        ParseFails("--force").ShouldContain("usage: synchro rollback");
    }

    [Fact]
    public void Parse_TwoSlaveNames_FailsWithUsage()
    {
        ParseFails("laptop", "desktop").ShouldContain("too many arguments");
    }
}
