using Shouldly;
using Synchro.Core;
using Synchro.Core.Install;
using Xunit;

namespace Synchro.Tests;

public class InstalledBinaryReplacerTests
{
    private static ReplaceOutcome Replace(string incoming, string target) =>
        InstalledBinaryReplacer.Replace(incoming, target)
            .ShouldBeOfType<Result<ReplaceOutcome>.Ok>().Value;

    [Fact]
    public void Replace_WhenTargetMissing_PlacesIncomingCopy()
    {
        var dir = TempDir.Create();
        try
        {
            var incoming = Path.Combine(dir, "incoming");
            var target = Path.Combine(dir, "synchro");
            File.WriteAllText(incoming, "payload-1");

            var outcome = Replace(incoming, target);

            outcome.UsedLockedTargetFallback.ShouldBeFalse();
            outcome.OldCleanupPending.ShouldBeFalse();
            File.Exists(target).ShouldBeTrue();
            File.ReadAllText(target).ShouldBe("payload-1");
            File.Exists(target + ".new").ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Replace_WhenTargetFileExists_OverwritesWithoutFallback()
    {
        var dir = TempDir.Create();
        try
        {
            var incoming = Path.Combine(dir, "incoming");
            var target = Path.Combine(dir, "synchro");
            File.WriteAllText(incoming, "payload-2");
            File.WriteAllText(target, "stale-payload");

            var outcome = Replace(incoming, target);

            outcome.UsedLockedTargetFallback.ShouldBeFalse();
            File.ReadAllText(target).ShouldBe("payload-2");
            File.Exists(target + ".new").ShouldBeFalse();
            File.Exists(target + ".old").ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Replace_WhenTargetCannotBeOverwritten_SwapsOldOutFirst()
    {
        var dir = TempDir.Create();
        try
        {
            var incoming = Path.Combine(dir, "incoming");
            var target = Path.Combine(dir, "synchro");
            File.WriteAllText(incoming, "payload-3");
            Directory.CreateDirectory(target);

            var outcome = Replace(incoming, target);

            outcome.UsedLockedTargetFallback.ShouldBeTrue();
            outcome.OldCleanupPending.ShouldBeFalse();
            File.Exists(target).ShouldBeTrue();
            File.ReadAllText(target).ShouldBe("payload-3");
            Directory.Exists(target).ShouldBeFalse();
            Directory.Exists(target + ".old").ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Replace_CleansUpStaleSideFilesFromCrashedRun()
    {
        var dir = TempDir.Create();
        try
        {
            var incoming = Path.Combine(dir, "incoming");
            var target = Path.Combine(dir, "synchro");
            File.WriteAllText(incoming, "payload-4");
            File.WriteAllText(target, "stale-payload");
            File.WriteAllText(target + ".new", "half-written");
            File.WriteAllText(target + ".old", "leftover");

            Replace(incoming, target);

            File.ReadAllText(target).ShouldBe("payload-4");
            File.Exists(target + ".new").ShouldBeFalse();
            File.Exists(target + ".old").ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Replace_WhenIncomingMissing_Fails()
    {
        var dir = TempDir.Create();
        try
        {
            var incoming = Path.Combine(dir, "ghost");
            var target = Path.Combine(dir, "synchro");

            var replace = InstalledBinaryReplacer.Replace(incoming, target);

            replace.ShouldBeOfType<Result<ReplaceOutcome>.Fail>();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
