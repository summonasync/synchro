using System.Text;
using Synchro.App.Commands;
using Synchro.Core;
using Synchro.Core.Git;
using Synchro.Core.Store;
using Synchro.Core.Sync;

namespace Synchro.App.Ui;

internal sealed record PendingConflict(string Path, string MasterContent, string SlaveContent);

internal sealed class UiSyncRunner
{
    private readonly object _gate = new();
    private readonly string _root;
    private readonly List<string> _log = [];
    private readonly Dictionary<string, PendingConflict> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<ConflictWinner>> _answers = new(StringComparer.Ordinal);

    public UiSyncRunner(string root) => _root = root;

    public string? ActiveSlave { get; private set; }

    public bool Running { get; private set; }

    public SyncRunReport? Report { get; private set; }

    public string? Error { get; private set; }

    public IReadOnlyList<string> Log
    {
        get
        {
            lock (_gate)
            {
                return [.. _log];
            }
        }
    }

    public IReadOnlyList<PendingConflict> Pending
    {
        get
        {
            lock (_gate)
            {
                return [.. _pending.Values];
            }
        }
    }

    public bool Start(string slaveName)
    {
        lock (_gate)
        {
            if (Running)
            {
                return false;
            }

            Running = true;
            ActiveSlave = slaveName;
            Report = null;
            Error = null;
            _log.Clear();
            _pending.Clear();
            _answers.Clear();
            _log.Add($"sync '{slaveName}' started");
        }

        Task.Run(() => Execute(slaveName));
        return true;
    }

    public bool TryAnswer(string path, ConflictWinner winner)
    {
        TaskCompletionSource<ConflictWinner>? tcs;
        lock (_gate)
        {
            if (!_answers.Remove(path, out tcs))
            {
                return false;
            }
        }

        AddLog($"'{path}' → {(winner == ConflictWinner.Master ? "master wins" : "slave wins")}");
        return tcs.TrySetResult(winner);
    }

    private void Execute(string slaveName)
    {
        try
        {
            var store = MasterStore.Load(_root);
            if (store is not Result<(StoreState State, byte[] Key)>.Ok storeOk)
            {
                Fail(((Result<(StoreState, byte[])>.Fail)store).Error.Message);
                return;
            }

            var resolved = SyncSlaveResolver.Resolve(storeOk.Value.State, slaveName);
            if (resolved is not Result<SlaveConfig>.Ok slaveOk)
            {
                Fail(((Result<SlaveConfig>.Fail)resolved).Error.Message);
                return;
            }

            var run = SyncOrchestrator.Run(
                _root, storeOk.Value.State, slaveOk.Value, new Bridge(this), nonInteractive: false);
            if (run is not Result<SyncRunOutput>.Ok runOk)
            {
                Fail(((Result<SyncRunOutput>.Fail)run).Error.Message);
                return;
            }

            StoreContainer.Save(StorePaths.Container(_root), storeOk.Value.Key, runOk.Value.State);
            lock (_gate)
            {
                Report = runOk.Value.Report;
                AddLog("sync finished — commit the inbox branches or roll back");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Fail(ex.Message);
        }
        finally
        {
            lock (_gate)
            {
                Running = false;
            }
        }
    }

    private void Fail(string message)
    {
        lock (_gate)
        {
            Error = message;
            AddLog($"sync failed: {message}");
        }
    }

    private void AddLog(string line)
    {
        lock (_gate)
        {
            _log.Add(line);
        }
    }

    private ConflictWinner Resolve(string path)
    {
        var pending = new PendingConflict(path, ReadHead(_root, path), ReadHead(SlaveRoot(), path));

        var tcs = new TaskCompletionSource<ConflictWinner>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _pending[path] = pending;
            _answers[path] = tcs;
        }

        AddLog($"conflict on '{path}' — waiting for the call");
        var winner = tcs.Task.GetAwaiter().GetResult();
        lock (_gate)
        {
            _pending.Remove(path);
        }

        return winner;
    }

    private string SlaveRoot()
    {
        if (ActiveSlave is null)
        {
            return string.Empty;
        }

        var store = MasterStore.Load(_root);
        return store is Result<(StoreState State, byte[] Key)>.Ok loaded
            && loaded.Value.State.Slaves.TryGetValue(ActiveSlave, out var config)
                ? config.Root
                : string.Empty;
    }

    private static string ReadHead(string repoRoot, string path)
    {
        if (repoRoot.Length == 0 || GitChangeService.GetFileContent(repoRoot, path) is not Result<byte[]>.Ok content)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(content.Value);
    }

    private sealed class Bridge(UiSyncRunner owner) : ISyncUi
    {
        public ConflictWinner ResolveConflict(string path) => owner.Resolve(path);
    }
}
