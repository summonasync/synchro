using Shouldly;
using Synchro.Core;
using Xunit;

namespace Synchro.Tests;

public class CanonicalPathTests
{
    [Fact]
    public void Resolve_PlainDirectory_ReturnsFullPathWithoutTrailingSeparator()
    {
        var dir = TempDir.Create();
        try
        {
            var resolved = CanonicalPath.Resolve(dir + Path.DirectorySeparatorChar, dir);

            resolved.ShouldBe(Path.GetFullPath(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Resolve_DotSegments_AreNormalizedAway()
    {
        var dir = TempDir.Create();
        try
        {
            var dotted = Path.Combine(dir, ".", "inner", "..");

            CanonicalPath.Resolve(dotted, dir).ShouldBe(Path.GetFullPath(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Resolve_SymlinkedDirectory_ReturnsTheLinkTarget()
    {
        var root = TempDir.Create();
        var real = Path.Combine(root, "real");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(real);
        try
        {
            Directory.CreateSymbolicLink(link, real);

            CanonicalPath.Resolve(link, root).ShouldBe(Path.GetFullPath(real));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Resolve_PathThroughSymlinkedAncestor_LandsInsideTheTarget()
    {
        var root = TempDir.Create();
        var real = Path.Combine(root, "real");
        var link = Path.Combine(root, "link");
        var nested = Path.Combine(real, "nested");
        Directory.CreateDirectory(nested);
        Directory.CreateSymbolicLink(link, real);
        try
        {
            CanonicalPath.Resolve(Path.Combine(link, "nested"), root)
                .ShouldBe(Path.GetFullPath(nested));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
