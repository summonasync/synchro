using System.Text;
using Synchro.Core;
using Synchro.Core.Store;

namespace Synchro.Core.Sync;

public static class SyncLedgerWriter
{
    public static Result<StoreState> Persist(
        StoreState state, string slaveName, LedgerHint hint, IReadOnlyList<SyncHistoryRecord> records)
    {
        var hinted = SlaveHints.Set(state, slaveName, Encoding.UTF8.GetBytes(LedgerHint.ToJson(hint)));
        if (hinted is not Result<StoreState>.Ok hintOk)
        {
            return new Result<StoreState>.Fail(((Result<StoreState>.Fail)hinted).Error);
        }

        var updated = hintOk.Value;
        foreach (var record in records)
        {
            var appended = SyncHistoryLog.Append(updated, slaveName, record);
            if (appended is not Result<StoreState>.Ok appendOk)
            {
                return new Result<StoreState>.Fail(((Result<StoreState>.Fail)appended).Error);
            }

            updated = appendOk.Value;
        }

        return new Result<StoreState>.Ok(updated);
    }
}
