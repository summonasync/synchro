using Shouldly;
using Synchro.App.Commands;
using Synchro.Core;
using Xunit;

namespace Synchro.Tests;

public class SyncArgsTests
{
    [Fact]
    public void Parse_SlaveNameAndYes_FillsBoth()
    {
        var parsed = SyncArgs.Parse(["laptop", "--yes"]);

        var ok = parsed.ShouldBeOfType<Result<SyncArgs>.Ok>();
        ok.Value.SlaveName.ShouldBe("laptop");
        ok.Value.AssumeYes.ShouldBeTrue();
        ok.Value.WantsHelp.ShouldBeFalse();
    }

    [Fact]
    public void Parse_NoArguments_BlankSlate()
    {
        var parsed = SyncArgs.Parse([]);

        var ok = parsed.ShouldBeOfType<Result<SyncArgs>.Ok>();
        ok.Value.SlaveName.ShouldBeNull();
        ok.Value.AssumeYes.ShouldBeFalse();
    }

    [Fact]
    public void Parse_Help_FlagsIt()
    {
        var parsed = SyncArgs.Parse(["--help"]);

        parsed.ShouldBeOfType<Result<SyncArgs>.Ok>().Value.WantsHelp.ShouldBeTrue();
    }

    [Fact]
    public void Parse_UnknownOption_FailsNamingIt()
    {
        var parsed = SyncArgs.Parse(["--force"]);

        var fail = parsed.ShouldBeOfType<Result<SyncArgs>.Fail>();
        fail.Error.Message.ShouldContain("--force");
    }

    [Fact]
    public void Parse_TwoPositionals_Fails()
    {
        var parsed = SyncArgs.Parse(["a", "b"]);

        parsed.ShouldBeOfType<Result<SyncArgs>.Fail>().Error.Message.ShouldContain("too many");
    }
}
