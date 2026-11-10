using Shouldly;
using Synchro.Core;
using Xunit;

namespace Synchro.Tests;

public class SyncManifest_BuildWithRequiredFields_ExposesValues
{
    [Fact]
    public void Manifest_AllFields_RoundTrip()
    {
        var manifest = new SyncManifest
        {
            SchemaVersion = 1,
            Channel = "slurpy",
            Mode = SyncMode.Git,
            BasePosition = new GitPosition("abc123def456"),
            NewPosition = new GitPosition("789abc123def"),
            Features =
            [
                new FeatureDeclaration
                {
                    Name = "checkout",
                    Description = "checkout flow rework",
                    Files = ["src/checkout.cs"]
                }
            ],
            Commits = [],
            Files = [],
            Deletions = []
        };

        manifest.SchemaVersion.ShouldBe(1);
        manifest.Channel.ShouldBe("slurpy");
        manifest.Mode.ShouldBe(SyncMode.Git);
        manifest.BasePosition.ShouldBe(new GitPosition("abc123def456"));
        manifest.Features.ShouldHaveSingleItem().Name.ShouldBe("checkout");
    }
}

public class Position_UnionVariants_DiscriminateCorrectly
{
    [Fact]
    public void Position_GitAndChain_PatternMatch()
    {
        Position git = new GitPosition("abc123def456");
        Position chain = new ChainPosition(7, "a1b2c3d4e5f6");

        git.ShouldBeOfType<GitPosition>();
        var asChain = chain.ShouldBeOfType<ChainPosition>();
        asChain.Seq.ShouldBe(7);
        asChain.Display.ShouldStartWith("#7:");
    }

    [Fact]
    public void Position_JsonRoundTrip_PreservesVariantAndData()
    {
        var manifest = new SyncManifest
        {
            SchemaVersion = 1,
            Channel = "slurpy",
            Mode = SyncMode.Git,
            BasePosition = new GitPosition("abc123def456"),
            NewPosition = new ChainPosition(3, "a1b2c3d4e5f6"),
            Features = [],
            Commits = [],
            Files = [],
            Deletions = []
        };

        var json = System.Text.Json.JsonSerializer.Serialize(manifest);
        var restored = System.Text.Json.JsonSerializer.Deserialize<SyncManifest>(json);

        restored.ShouldNotBeNull();
        restored.BasePosition.ShouldBe(new GitPosition("abc123def456"));
        restored.NewPosition.ShouldBe(new ChainPosition(3, "a1b2c3d4e5f6"));
    }
}

public class Result_OkAndFailVariants_CarryValueOrError
{
    [Fact]
    public void Result_EitherVariant_Matches()
    {
        Result<int> ok = new Result<int>.Ok(42);
        Result<int> fail = new Result<int>.Fail(SyncError.Invalid("bad input"));

        var asOk = ok.ShouldBeOfType<Result<int>.Ok>();
        asOk.Value.ShouldBe(42);

        var asFail = fail.ShouldBeOfType<Result<int>.Fail>();
        asFail.Error.Code.ShouldBe("invalid");
    }
}
