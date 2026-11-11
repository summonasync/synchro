using System.Security.Cryptography;
using System.Text.Json;
using Shouldly;
using Synchro.App.Mcp;
using Synchro.Core;
using Synchro.Core.Pack;
using Synchro.Core.Package;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class McpToolsTests
{
    private static string InitSide(string dir, string? keyHex = null) =>
        StoreInitializer.Initialize(dir, new InitOptions { KeyHex = keyHex, Peer = "shiva" })
            .ShouldBeOfType<Result<InitOutcome>.Ok>().Value.GeneratedKeyHex
            ?? "adopted";

    private static void Write(string dir, string path, string content)
    {
        var fullPath = Path.Combine(dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    [Fact]
    public void Status_WithoutStore_ReportsError()
    {
        var dir = TempDir.Create();
        try
        {
            var json = new SynchroMcpTools(dir).Status();

            var doc = Parse(json);
            doc.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
            doc.RootElement.GetProperty("error").GetString().ShouldNotBeNull().ShouldContain("synchro init");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void PackDraft_Declare_Seal_ProducesNeutralPackage()
    {
        var dirA = TempDir.Create();
        try
        {
            InitSide(dirA);
            Write(dirA, "a.txt", "alpha\n");
            Write(dirA, "b.txt", "beta\n");
            var tools = new SynchroMcpTools(dirA);

            var draftJson = tools.PackDraft();
            var draft = Parse(draftJson);
            draft.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
            draft.RootElement.GetProperty("files").GetArrayLength().ShouldBe(2);

            var declaredJson = tools.PackDeclare("docs", "initial docs", "a.txt,b.txt");
            var declared = Parse(declaredJson);
            declared.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
            declared.RootElement.GetProperty("undeclared").GetArrayLength().ShouldBe(0);

            var sealedJson = tools.PackSeal();
            var sealedDoc = Parse(sealedJson);
            sealedDoc.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
            var packagePath = sealedDoc.RootElement.GetProperty("package").GetString()!;
            File.Exists(packagePath).ShouldBeTrue();
            packagePath.ShouldEndWith(".synchr");
        }
        finally
        {
            Directory.Delete(dirA, true);
        }
    }

    [Fact]
    public void PackDeclare_UnknownAndDuplicateFiles_AreRejected()
    {
        var dirA = TempDir.Create();
        try
        {
            InitSide(dirA);
            Write(dirA, "a.txt", "alpha\n");
            var tools = new SynchroMcpTools(dirA);

            tools.PackDraft();
            var unknownJson = tools.PackDeclare("docs", "d", "ghost.txt");
            Parse(unknownJson).RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();

            var okJson = tools.PackDeclare("docs", "d", "a.txt");
            Parse(okJson).RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();

            var duplicateNameJson = tools.PackDeclare("docs", "again", "a.txt");
            Parse(duplicateNameJson).RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();

            tools.PackDraft();
            var reDraftJson = tools.PackDeclare("docs", "d", "a.txt");
            Parse(reDraftJson).RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(dirA, true);
        }
    }

    [Fact]
    public void ApplyStart_InvalidatesStaleDraft()
    {
        var dirA = TempDir.Create();
        var dirB = TempDir.Create();
        try
        {
            var keyHex = InitSide(dirA);
            InitSide(dirB, keyHex);
            Write(dirA, "a.txt", "alpha\n");
            var toolsA = new SynchroMcpTools(dirA);
            var toolsB = new SynchroMcpTools(dirB);

            toolsA.PackDraft();
            var pkg1Json = toolsA.PackSeal();
            var pkg1 = Parse(pkg1Json).RootElement.GetProperty("package").GetString()!;
            toolsB.ApplyStart(pkg1);

            File.WriteAllText(Path.Combine(dirA, "a.txt"), "alpha2\n");
            var stateA1 = PackService.Pack(dirA, PlainStore(), new PackOptions())
                .ShouldBeOfType<Result<PackDraft>.Ok>().Value.UpdatedState!;
            File.WriteAllText(Path.Combine(dirA, "b.txt"), "bee\n");
            var pkg2 = Path.Combine(dirA, "s2.synchr");
            var draft2 = PackService.Pack(dirA, stateA1, new PackOptions())
                .ShouldBeOfType<Result<PackDraft>.Ok>().Value;
            PackageSealer.Seal(pkg2, Convert.FromHexString(keyHex), draft2.Manifest);

            var toolsBBefore = toolsB.PackDraft();
            Parse(toolsBBefore).RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();

            toolsB.ApplyStart(pkg2);

            var sealedAfter = toolsB.PackSeal();
            Parse(sealedAfter).RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Apply_SceneLifecycle_RunsThroughTools()
    {
        var dirA = TempDir.Create();
        var dirB = TempDir.Create();
        try
        {
            var keyHex = InitSide(dirA);
            InitSide(dirB, keyHex);
            Write(dirA, "a.txt", "alpha\n");
            Write(dirA, "b.txt", "beta\n");

            var toolsA = new SynchroMcpTools(dirA);
            var toolsB = new SynchroMcpTools(dirB);

            toolsA.PackDraft();
            var pkg1Json = toolsA.PackSeal();
            var pkg1 = Parse(pkg1Json).RootElement.GetProperty("package").GetString()!;

            var appliedJson = toolsB.ApplyStart(pkg1);
            Parse(appliedJson).RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();

            var stateA1 = PackService.Pack(dirA, PlainStore(), new PackOptions())
                .ShouldBeOfType<Result<PackDraft>.Ok>().Value.UpdatedState!;
            File.WriteAllText(Path.Combine(dirA, "a.txt"), "A version\n");
            var pkg2 = Path.Combine(dirA, "s2.synchr");
            var draft2 = PackService.Pack(dirA, stateA1, new PackOptions())
                .ShouldBeOfType<Result<PackDraft>.Ok>().Value;
            PackageSealer.Seal(pkg2, Convert.FromHexString(keyHex), draft2.Manifest);

            var stateB = StoreContainer.Load(Path.Combine(dirB, ".synchro", "container"), Convert.FromHexString(keyHex))
                .ShouldBeOfType<Result<StoreState>.Ok>().Value;
            File.WriteAllText(Path.Combine(dirB, "a.txt"), "B version\n");
            var conflictJson = toolsB.ApplyStart(pkg2);
            conflictJson.ShouldContain("scenePending");
            var conflict = Parse(conflictJson);
            conflict.RootElement.GetProperty("scenePending").GetBoolean().ShouldBeTrue();

            var statusJson = toolsB.SceneStatus();
            var status = Parse(statusJson);
            status.RootElement.GetProperty("unresolved").GetArrayLength().ShouldBe(1);

            var readJson = toolsB.SceneRead("a.txt");
            var read = Parse(readJson);
            read.RootElement.GetProperty("theirs").GetString().ShouldNotBeNull().ShouldContain("A version");
            read.RootElement.GetProperty("ours").GetString().ShouldNotBeNull().ShouldContain("B version");

            var resolveJson = toolsB.SceneResolve("a.txt", "replace");
            Parse(resolveJson).RootElement.GetProperty("unresolved").GetArrayLength().ShouldBe(0);

            var commitJson = toolsB.ApplyCommit();
            Parse(commitJson).RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();

            var logJson = toolsB.Log();
            Parse(logJson).RootElement.GetProperty("events").GetArrayLength().ShouldBeGreaterThanOrEqualTo(2);
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }

    private static StoreState PlainStore() => new()
    {
        SchemaVersion = 1,
        Channel = new ChannelConfig { Name = "docs", Peer = "shiva", Mode = SyncMode.Plain },
        Ledger = [],
        AppliedFileHashes = [],
        AppliedContents = []
    };

}
