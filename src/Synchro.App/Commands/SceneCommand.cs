using Synchro.Core;
using Synchro.Core.Apply;
using Synchro.Core.Scene;
using Synchro.Core.Store;

namespace Synchro.App.Commands;

internal static class SceneCommand
{
    public static int Run(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        if (!Directory.Exists(StorePaths.Folder(root)) || !StoreInitializer.TryLoadKey(root, out var key))
        {
            Console.Error.WriteLine("no store here — run 'synchro init' first");
            return 1;
        }

        var loaded = StoreContainer.Load(StorePaths.Container(root), key);
        if (loaded is not Result<StoreState>.Ok state)
        {
            Console.Error.WriteLine($"store unreadable: {((Result<StoreState>.Fail)loaded).Error.Message}");
            return 1;
        }

        if (args.Length == 0)
        {
            return PrintStatus(state.Value);
        }

        if (args[0] == "resolve" && args.Length > 3)
        {
            return Usage("too many arguments — usage: synchro scene resolve <path> <drop|replace|merge|resolved>");
        }

        return args[0] switch
        {
            "resolve" when args.Length >= 3 => Resolve(root, state.Value, key, args[1], args[2]),
            "resolve" => Usage("usage: synchro scene resolve <path> <drop|replace|merge|resolved>"),
            "commit" => Commit(root, state.Value, key),
            _ => Usage($"unknown scene command '{args[0]}' — use resolve or commit")
        };
    }

    private static int PrintStatus(StoreState state)
    {
        var status = SceneService.Status(state);
        if (status is not Result<SceneStatus>.Ok ok)
        {
            Console.Error.WriteLine(((Result<SceneStatus>.Fail)status).Error.Message);
            return 1;
        }

        var value = ok.Value;
        Console.WriteLine($"conflict scene: {value.SceneDir}");
        foreach (var path in value.ConflictPaths)
        {
            var resolution = value.Resolutions.TryGetValue(path, out var r) ? r : "unresolved";
            Console.WriteLine($"  {path} — {resolution}");
        }

        Console.WriteLine(value.Unresolved.Count == 0
            ? "all conflicts resolved — run 'synchro scene commit' to finalize"
            : $"{value.Unresolved.Count} unresolved — synchro scene resolve <path> <drop|replace|merge|resolved>");
        return 0;
    }

    private static int Resolve(string root, StoreState state, byte[] key, string path, string op)
    {
        var resolved = SceneService.Resolve(root, state, path, op);
        if (resolved is not Result<StoreState>.Ok ok)
        {
            Console.Error.WriteLine($"resolve failed: {((Result<StoreState>.Fail)resolved).Error.Message}");
            return 1;
        }

        StoreContainer.Save(StorePaths.Container(Directory.GetCurrentDirectory()), key, ok.Value);
        Console.WriteLine($"resolved '{path}' as '{op}'");

        if (op == SceneService.OpMerge)
        {
            Console.WriteLine("merge draft written into the working file — resolve the markers, then:");
            Console.WriteLine($"  synchro scene resolve {path} resolved");
        }

        var status = SceneService.Status(ok.Value);
        var remaining = status is Result<SceneStatus>.Ok s ? s.Value.Unresolved.Count : 0;
        Console.WriteLine(remaining == 0
            ? "all conflicts resolved — run 'synchro scene commit' to finalize"
            : $"{remaining} conflict path(s) still unresolved");
        return 0;
    }

    private static int Commit(string root, StoreState state, byte[] key)
    {
        var committed = SceneService.Commit(root, state, key);
        if (committed is not Result<ApplyReport>.Ok ok)
        {
            Console.Error.WriteLine($"commit failed: {((Result<ApplyReport>.Fail)committed).Error.Message}");
            return 1;
        }

        StoreContainer.Save(StorePaths.Container(root), key, ok.Value.UpdatedState!);
        Console.WriteLine("scene committed — sync state advanced, ledger updated");
        Console.WriteLine($"  resolved paths: {ok.Value.ConflictPaths.Count}");
        foreach (var warning in ok.Value.Warnings)
        {
            Console.WriteLine($"  warning: {warning}");
        }

        return 0;
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}
