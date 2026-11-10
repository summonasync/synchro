using System.Text.Json;
using System.Text.Json.Serialization;
using Synchro.Core;
using Synchro.Core.Pack;
using Synchro.Core.Store;

namespace Synchro.App.Commands;

internal static class PackCommand
{
    private static readonly JsonSerializerOptions DraftJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static int Run(string[] args)
    {
        var full = false;
        string? outPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--full":
                    full = true;
                    break;
                case "--out" when i + 1 < args.Length:
                    outPath = args[++i];
                    break;
                case "--out":
                    Console.Error.WriteLine("missing value for '--out'");
                    return 2;
                default:
                    Console.Error.WriteLine($"unknown option '{args[i]}'");
                    return 2;
            }
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

        var draft = PackService.Pack(root, state.Value, new PackOptions { Full = full });
        if (draft is not Result<PackDraft>.Ok packOk)
        {
            Console.Error.WriteLine($"pack failed: {((Result<PackDraft>.Fail)draft).Error.Message}");
            return 1;
        }

        var value = packOk.Value;
        var manifest = value.Manifest;
        var target = outPath ?? Path.Combine(root, StorePaths.FolderName, "last-pack-draft.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        File.WriteAllText(target, JsonSerializer.Serialize(manifest, DraftJsonOptions));

        Console.WriteLine(manifest.BasePosition is null
            ? "pack draft: baseline (full snapshot)"
            : $"pack draft: base {manifest.BasePosition.Display}");
        Console.WriteLine($"  head: {manifest.NewPosition.Display}");
        Console.WriteLine($"  files: {manifest.Files.Count}, deletions: {manifest.Deletions.Count}, commits: {manifest.Commits.Count}");
        Console.WriteLine("  features: none declared yet (declare before sealing)");
        foreach (var warning in value.Warnings)
        {
            Console.WriteLine($"  warning: {warning}");
        }

        Console.WriteLine($"  draft written: {target}");
        return 0;
    }
}
