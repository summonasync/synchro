using Shouldly;
using Synchro.Core.Install;
using Xunit;

namespace Synchro.Tests;

public class LinuxPathProbeTests
{
    [Theory]
    [InlineData("/opt/tools/bin:/usr/local/bin", "/opt/tools/bin", true)]
    [InlineData("/usr/local/bin", "/opt/tools/bin", false)]
    [InlineData("/opt/tools/bin-extra:/usr/bin", "/opt/tools/bin", false)]
    [InlineData("/opt/tools/bin::/usr/bin", "/opt/tools/bin", true)]
    [InlineData("", "/opt/tools/bin", false)]
    public void Contains_ChecksExactColonSeparatedEntries(string pathValue, string directory, bool expected) =>
        LinuxPathProbe.Contains(pathValue, directory).ShouldBe(expected);

    [Fact]
    public void Contains_WhenPathValueNull_ReturnsFalse() =>
        LinuxPathProbe.Contains(null, "/opt/tools/bin").ShouldBeFalse();
}
