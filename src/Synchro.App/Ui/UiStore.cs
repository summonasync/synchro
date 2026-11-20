namespace Synchro.App.Ui;

internal interface IUiStore
{
    IReadOnlyList<SlaveView> GetSlaves();

    IReadOnlyList<HistoryEntryView> GetHistory(string slaveName);

    SlaveConflictView? GetConflicts(string slaveName);
}
