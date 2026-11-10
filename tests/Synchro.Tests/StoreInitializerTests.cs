using System.Security.Cryptography;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class StoreInitializerTests
{
    [Fact]
    public void Initialize_PlainDirectory_CreatesPlainStoreWithGeneratedKey()
    {
        var dir = TempDir.Create();
        try
        {
            var outcome = StoreInitializer.Initialize(dir, new InitOptions { Peer = "shiva" });

            var ok = outcome.ShouldBeOfType<Result<InitOutcome>.Ok>();
            ok.Value.Mode.ShouldBe(SyncMode.Plain);
            ok.Value.KeyGenerated.ShouldBeTrue();
            ok.Value.GeneratedKeyHex.ShouldNotBeNull();
            ok.Value.GeneratedKeyHex.Length.ShouldBe(64);
            File.Exists(StorePaths.Key(dir)).ShouldBeTrue();
            File.Exists(StorePaths.Container(dir)).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Initialize_GitRepository_WritesExcludeLinesAndHidesStore()
    {
        var dir = TempDir.Create();
        try
        {
            GitCli.Init(dir);

            var outcome = StoreInitializer.Initialize(dir, new InitOptions { Peer = "shiva" });

            var ok = outcome.ShouldBeOfType<Result<InitOutcome>.Ok>();
            ok.Value.Mode.ShouldBe(SyncMode.Git);
            ok.Value.ExcludeLinesWritten.ShouldBe(["/.synchro/", "*.synchr"]);

            var exclude = File.ReadAllText(Path.Combine(dir, ".git", "info", "exclude"));
            exclude.ShouldContain("/.synchro/");
            exclude.ShouldContain("*.synchr");

            GitCli.StatusPorcelain(dir).Trim().ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Initialize_ExistingStore_FailsWithConflict()
    {
        var dir = TempDir.Create();
        try
        {
            StoreInitializer.Initialize(dir, new InitOptions());

            var second = StoreInitializer.Initialize(dir, new InitOptions());

            var fail = second.ShouldBeOfType<Result<InitOutcome>.Fail>();
            fail.Error.Code.ShouldBe("conflict");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Initialize_StorePathIsFile_FailsWithConflictInsteadOfCrash()
    {
        var dir = TempDir.Create();
        try
        {
            File.WriteAllText(Path.Combine(dir, ".synchro"), "not a store");

            var outcome = StoreInitializer.Initialize(dir, new InitOptions());

            var fail = outcome.ShouldBeOfType<Result<InitOutcome>.Fail>();
            fail.Error.Code.ShouldBe("conflict");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Initialize_FilesystemRoot_FailsBeforeWriting()
    {
        var outcome = StoreInitializer.Initialize(
            Path.DirectorySeparatorChar.ToString(), new InitOptions());

        var fail = outcome.ShouldBeOfType<Result<InitOutcome>.Fail>();
        fail.Error.Code.ShouldBe("invalid");
        fail.Error.Message.ShouldContain("project directory");
    }

    [Fact]
    public void Initialize_AdoptedPeerKey_ProducesInteroperableStores()
    {
        var dirA = TempDir.Create();
        var dirB = TempDir.Create();
        try
        {
            var sideA = StoreInitializer.Initialize(dirA, new InitOptions { Peer = "shiva" });
            var keyHex = sideA.ShouldBeOfType<Result<InitOutcome>.Ok>().Value.GeneratedKeyHex;

            var sideB = StoreInitializer.Initialize(dirB, new InitOptions { KeyHex = keyHex, Peer = "khadgar" });
            var okB = sideB.ShouldBeOfType<Result<InitOutcome>.Ok>();
            okB.Value.KeyGenerated.ShouldBeFalse();

            var stateA = StoreContainer.Load(StorePaths.Container(dirA), File.ReadAllBytes(StorePaths.Key(dirB)));
            stateA.ShouldBeOfType<Result<StoreState>.Ok>();
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Initialize_AdoptedKey_OutcomeCarriesNoHex()
    {
        var dir = TempDir.Create();
        try
        {
            var adopted = StoreInitializer.Initialize(dir, new InitOptions
            {
                KeyHex = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))
            });

            var ok = adopted.ShouldBeOfType<Result<InitOutcome>.Ok>();
            ok.Value.GeneratedKeyHex.ShouldBeNull();
            ok.Value.KeyGenerated.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Initialize_InvalidKeyHex_FailsWithInvalid(string badHex)
    {
        var dir = TempDir.Create();
        try
        {
            var outcome = StoreInitializer.Initialize(dir, new InitOptions { KeyHex = badHex });

            var fail = outcome.ShouldBeOfType<Result<InitOutcome>.Fail>();
            fail.Error.Code.ShouldBe("invalid");
            fail.Error.Message.ShouldContain("hex");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
