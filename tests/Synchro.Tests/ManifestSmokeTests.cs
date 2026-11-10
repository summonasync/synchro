using Shouldly;
using Synchro.Core;
using Xunit;

namespace Synchro.Tests;

public class ManifestSmokeTests
{
    [Fact]
    public void ManifestCarriesRequiredFields()
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

    [Fact]
    public void PositionUnionDiscriminatesVariants()
    {
        Position git = new GitPosition("abc123def456");
        Position chain = new ChainPosition(7, "a1b2c3d4e5f6");

        git.ShouldBeOfType<GitPosition>();
        chain.ShouldBeOfType<ChainPosition>();

        var asChain = chain.ShouldBeOfType<ChainPosition>();
        asChain.Seq.ShouldBe(7);
        asChain.Display.ShouldStartWith("#7:");
    }

    [Fact]
    public void ResultUnionCarriesValueOrError()
    {
        Result<int> ok = new Result<int>.Ok(42);
        Result<int> fail = new Result<int>.Fail(SyncError.Invalid("bad input"));

        var asOk = ok.ShouldBeOfType<Result<int>.Ok>();
        asOk.Value.ShouldBe(42);

        var asFail = fail.ShouldBeOfType<Result<int>.Fail>();
        asFail.Error.Code.ShouldBe("invalid");
    }
}
