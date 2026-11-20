using System.Text;
using Shouldly;
using Synchro.Core;
using Synchro.Core.Landing;
using Synchro.Core.Store;
using Synchro.Core.Sync;
using Xunit;

namespace Synchro.Tests;

public class SyncStatusReaderTests
{
    [Fact]
    public void Read_FreshSlave_EmptyHintAndCleanInboxes()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            var config = SlaveRegistry.List(state).Single();

            var status = SyncStatusReader.Read(master, state, config);

            status.Hint.ShouldBeNull();
            status.InFlight.ShouldBeFalse();
            status.LastOperation.ShouldBeNull();
            status.SlaveInbox.ShouldNotBeNull();
            status.SlaveInbox!.Branch.ShouldBe("main");
            status.SlaveInbox.OnSyncBranch.ShouldBeFalse();
            status.SlaveInbox.DirtyFiles.ShouldBe(0);
            status.MasterInbox.ShouldNotBeNull();
            status.MasterInbox!.Branch.ShouldBe("main");
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }

    [Fact]
    public void Read_ParkedOnSyncBranchWithDraft_ShowsOpenInbox()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            var config = SlaveRegistry.List(state).Single();
            GitCli.Run(master, "checkout", "-b", "feature-sync");
            File.WriteAllText(Path.Combine(master, "draft.txt"), "inbox draft");

            var status = SyncStatusReader.Read(master, state, config);

            status.MasterInbox.ShouldNotBeNull();
            status.MasterInbox!.OnSyncBranch.ShouldBeTrue();
            status.MasterInbox.Branch.ShouldBe("feature-sync");
            status.MasterInbox.DirtyFiles.ShouldBeGreaterThanOrEqualTo(1);
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }

    [Fact]
    public void Read_HintHistoryAndInFlight_RoundTripFromStore()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            var config = SlaveRegistry.List(state).Single();
            var hint = new LedgerHint
            {
                Relation = HistoryRelation.Related,
                MergeBase = null,
                MasterHead = "aaa1111",
                SlaveHead = "bbb2222",
                SlaveSyncHead = null,
                ConsumedShas = new List<string> { "ccc3333" },
                PendingShas = new List<string> { "ddd4444" },
                DeSync = false
            };
            state = SlaveHints.Set(state, "laptop", Encoding.UTF8.GetBytes(LedgerHint.ToJson(hint)))
                .ShouldBeOfType<Result<StoreState>.Ok>().Value;
            var record = new SyncHistoryRecord
            {
                AtUtc = DateTimeOffset.Parse("2026-11-20T10:00:00Z"),
                Direction = SyncDirection.MasterToSlave,
                FilesChanged = 3,
                Conflicts = [new SyncConflict { Path = "shared.txt", Winner = ConflictWinner.Master }],
                RolledBack = false
            };
            state = SyncHistoryLog.Append(state, "laptop", record).ShouldBeOfType<Result<StoreState>.Ok>().Value;
            InFlightMarker.Set(InFlightMarker.PathFor(master, "laptop"));

            var status = SyncStatusReader.Read(master, state, config);

            status.InFlight.ShouldBeTrue();
            status.Hint.ShouldBeEquivalentTo(hint);
            status.LastOperation.ShouldBeEquivalentTo(record);

            InFlightMarker.Clear(InFlightMarker.PathFor(master, "laptop"));
            SyncStatusReader.Read(master, state, config).InFlight.ShouldBeFalse();
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }

    [Fact]
    public void Read_MissingSlaveRepo_SlaveInboxNull()
    {
        var (master, slave, _, state) = TestSyncPair.Create();
        try
        {
            var config = SlaveRegistry.List(state).Single();
            Directory.Delete(slave, true);

            var status = SyncStatusReader.Read(master, state, config);

            status.SlaveInbox.ShouldBeNull();
            status.MasterInbox.ShouldNotBeNull();
        }
        finally
        {
            TestSyncPair.Cleanup(master);
        }
    }
}
