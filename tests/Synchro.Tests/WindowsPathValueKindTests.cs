using Shouldly;
using Synchro.Core.Install;
using Xunit;

namespace Synchro.Tests;

public class WindowsPathValueKindTests
{
    [Fact]
    public void ProbeFailureDefaultsToExpandString_WhenValueIsMissing_ReturnsTrue()
    {
        WindowsPathValueKind.ProbeFailureDefaultsToExpandString(
            new ArgumentException("Environment key carries no PATH value")).ShouldBeTrue();
    }

    [Fact]
    public void ProbeFailureDefaultsToExpandString_WhenKeyIsInaccessible_ReturnsTrue()
    {
        WindowsPathValueKind.ProbeFailureDefaultsToExpandString(
            new System.Security.SecurityException("registry key is not accessible")).ShouldBeTrue();
    }

    [Fact]
    public void ProbeFailureDefaultsToExpandString_WhenReadThrowsIo_ReturnsTrue()
    {
        WindowsPathValueKind.ProbeFailureDefaultsToExpandString(new IOException("registry io failure")).ShouldBeTrue();
    }

    [Fact]
    public void ProbeFailureDefaultsToExpandString_WhenAccessIsDenied_ReturnsFalse()
    {
        WindowsPathValueKind.ProbeFailureDefaultsToExpandString(new UnauthorizedAccessException()).ShouldBeFalse();
    }
}
