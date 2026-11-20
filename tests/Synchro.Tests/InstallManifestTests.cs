using Shouldly;
using Synchro.Core;
using Synchro.Core.Install;
using Xunit;

namespace Synchro.Tests;

public class InstallManifestTests
{
    [Fact]
    public void Read_WhenManifestMissing_ReturnsNullVersion()
    {
        var root = TempDir.Create();
        try
        {
            var read = InstallManifest.Read(root);

            read.ShouldBeOfType<Result<string?>.Ok>();
            ((Result<string?>.Ok)read).Value.ShouldBeNull();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Write_ThenRead_RoundTripsVersion()
    {
        var root = TempDir.Create();
        try
        {
            var write = InstallManifest.Write(root, "9.8.7");

            write.ShouldBeOfType<Result<string>.Ok>();
            InstallManifest.Read(root).ShouldBeOfType<Result<string?>.Ok>().Value.ShouldBe("9.8.7");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Write_CreatesMissingRootDirectory()
    {
        var home = TempDir.Create();
        var root = Path.Combine(home, "nested", "synchro");
        try
        {
            InstallManifest.Write(root, "1.2.3").ShouldBeOfType<Result<string>.Ok>();

            File.Exists(InstallManifest.PathFor(root)).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    [Fact]
    public void Read_WhenManifestCorrupt_Fails()
    {
        var root = TempDir.Create();
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(InstallManifest.PathFor(root), "not json at all {");

            var read = InstallManifest.Read(root);

            read.ShouldBeOfType<Result<string?>.Fail>();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
