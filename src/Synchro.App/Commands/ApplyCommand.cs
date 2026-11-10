using Synchro.Core;
using Synchro.Core.Apply;
using Synchro.Core.Store;

namespace Synchro.App.Commands;

internal static class ApplyCommand
{
    public static int Run(string[] args)
    {
        var packagePath = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (packagePath is null)
        {
            Console.Error.WriteLine("usage: synchro apply <package.synchr>");
            return 2;
        }

        if (!File.Exists(packagePath))
        {
            Console.Error.WriteLine($"package not found: '{packagePath}'");
            return 1;
        }

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

        var report = ApplyService.Apply(root, state.Value, key, packagePath);
        if (report is not Result<ApplyReport>.Ok ok)
        {
            Console.Error.WriteLine($"apply failed: {((Result<ApplyReport>.Fail)report).Error.Message}");
            return 1;
        }

        var value = ok.Value;
        if (value.UpdatedState is { } updated)
        {
            StoreContainer.Save(StorePaths.Container(root), key, updated);
        }

        Console.WriteLine($"apply: {packagePath}");

        if (value.AlreadyApplied)
        {
            Console.WriteLine("  already applied — nothing to do");
            return 0;
        }

        Console.WriteLine($"  applied: {value.AppliedFiles.Count} files, {value.AppliedDeletions.Count} deletions");

        if (value.ScenePending)
        {
            Console.WriteLine($"  CONFLICTS: {value.ConflictPaths.Count} path(s) staged — resolution lands with the conflict dialogue");
            foreach (var path in value.ConflictPaths)
            {
                Console.WriteLine($"    conflict: {path}");
            }
        }

        foreach (var warning in value.Warnings)
        {
            Console.WriteLine($"  warning: {warning}");
        }

        return 0;
    }
}
