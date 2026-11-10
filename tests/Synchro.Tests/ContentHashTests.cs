using Shouldly;
using Synchro.Core;
using Synchro.Core.Hashing;
using Xunit;

namespace Synchro.Tests;

public class ContentHashTests
{
    [Fact]
    public void Compute_CrlfAndLfVariants_ProduceSameHash()
    {
        var crlf = "first line\r\nsecond line\r\n"u8.ToArray();
        var lf = "first line\nsecond line\n"u8.ToArray();
        var crOnly = "first line\rsecond line\r"u8.ToArray();

        ContentHash.Compute(crlf).ShouldBe(ContentHash.Compute(lf));
        ContentHash.Compute(crlf).ShouldBe(ContentHash.Compute(crOnly));
    }

    [Fact]
    public void Compute_PlainContent_MatchesRawSha256Hex()
    {
        var content = "no carriage returns here\n"u8.ToArray();
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));

        ContentHash.Compute(content).ShouldBe(expected);
        ContentHash.Compute(content).Length.ShouldBe(64);
        ContentHash.Compute(content).ToCharArray().ShouldAllBe(c => !char.IsUpper(c));
    }

    [Fact]
    public void Normalize_MixedLineEndings_YieldsLfOnly()
    {
        var mixed = "a\r\nb\rc\nd"u8.ToArray();

        var normalized = ContentHash.Normalize(mixed);

        System.Text.Encoding.UTF8.GetString(normalized).ShouldBe("a\nb\nc\nd");
    }
}
