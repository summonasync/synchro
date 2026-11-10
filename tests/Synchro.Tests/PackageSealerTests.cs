using System.Security.Cryptography;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Package;
using Xunit;

namespace Synchro.Tests;

public class PackageSealerTests
{
    private static SyncManifest SampleManifest() => new()
    {
        SchemaVersion = 1,
        Channel = "slurpy",
        Mode = Synchro.Core.SyncMode.Plain,
        BasePosition = new ChainPosition(1, "root111"),
        NewPosition = new ChainPosition(2, "root222"),
        Features = [],
        Commits = [],
        Files =
        [
            new PackageFileEntry
            {
                Path = "src/app.cs",
                Hash = "aa22",
                Content = "Console.WriteLine(42);"u8.ToArray()
            }
        ],
        Deletions = ["old.txt"]
    };

    [Fact]
    public void Open_SealedPackage_RoundTripsManifest()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "pkg.synchr");
            var key = RandomNumberGenerator.GetBytes(32);

            PackageSealer.Seal(path, key, SampleManifest());
            var opened = PackageSealer.Open(path, key);

            var ok = opened.ShouldBeOfType<Result<SyncManifest>.Ok>();
            ok.Value.Channel.ShouldBe("slurpy");
            ok.Value.NewPosition.ShouldBe(new ChainPosition(2, "root222"));
            ok.Value.Files.ShouldHaveSingleItem().Path.ShouldBe("src/app.cs");
            ok.Value.Deletions.ShouldBe(["old.txt"]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void PackageBytes_NeutralNoPlaintextNoMachineTraces()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "pkg.synchr");
            PackageSealer.Seal(path, RandomNumberGenerator.GetBytes(32), SampleManifest());
            var bytes = File.ReadAllBytes(path);

            var utf8 = System.Text.Encoding.UTF8.GetString(bytes);
            var utf16Le = System.Text.Encoding.Unicode.GetString(bytes);

            utf8.ShouldNotContain("slurpy");
            utf8.ShouldNotContain("src/app.cs");
            utf8.ShouldNotContain(Environment.MachineName);
            utf8.ShouldNotContain(Environment.UserName);
            utf16Le.ShouldNotContain("slurpy");
            bytes.Length.ShouldBeGreaterThan(64);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Seal_SameManifestDifferentKeys_YieldsDifferentBytes()
    {
        var dir = TempDir.Create();
        try
        {
            var pathA = Path.Combine(dir, "a.synchr");
            var pathB = Path.Combine(dir, "b.synchr");

            PackageSealer.Seal(pathA, RandomNumberGenerator.GetBytes(32), SampleManifest());
            PackageSealer.Seal(pathB, RandomNumberGenerator.GetBytes(32), SampleManifest());

            File.ReadAllBytes(pathA).SequenceEqual(File.ReadAllBytes(pathB)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Open_WrongKey_ReturnsCleanFailure()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "pkg.synchr");
            PackageSealer.Seal(path, RandomNumberGenerator.GetBytes(32), SampleManifest());

            var opened = PackageSealer.Open(path, RandomNumberGenerator.GetBytes(32));

            var fail = opened.ShouldBeOfType<Result<SyncManifest>.Fail>();
            fail.Error.Message.ShouldContain("wrong key");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Open_TruncatedFile_FailsCleanly()
    {
        var dir = TempDir.Create();
        try
        {
            var path = Path.Combine(dir, "pkg.synchr");
            File.WriteAllBytes(path, [0x01, 0x02]);

            var opened = PackageSealer.Open(path, RandomNumberGenerator.GetBytes(32));

            opened.ShouldBeOfType<Result<SyncManifest>.Fail>();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
