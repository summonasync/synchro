using Synchro.Core;
using Synchro.Core.Store;

namespace Synchro.App.Commands;

internal static class InitCommand
{
    public static int Run(string[] args)
    {
        if (!TryParseOptions(args, out var keyHex, out var peer, out var channel, out var parseError))
        {
            Console.Error.WriteLine(parseError);
            return 2;
        }

        var outcome = StoreInitializer.Initialize(
            Directory.GetCurrentDirectory(),
            new InitOptions { KeyHex = keyHex, Peer = peer, ChannelName = channel });

        if (outcome is not Result<InitOutcome>.Ok ok)
        {
            var error = ((Result<InitOutcome>.Fail)outcome).Error;
            Console.Error.WriteLine($"init failed: {error.Message}");
            return 1;
        }

        var value = ok.Value;
        Console.WriteLine($"mode: {(value.Mode == SyncMode.Git ? "git" : "plain")}");
        Console.WriteLine($"channel: {value.Channel} (peer: {value.Peer})");

        if (value.KeyGenerated)
        {
            Console.WriteLine("new channel key generated — share it with your peer out-of-band (never over the sync channel):");
            Console.WriteLine(value.GeneratedKeyHex);
            Console.WriteLine("peer side runs: synchro init --key <the-hex-above> --peer <your-name>");
            Console.WriteLine("the key is stored locally in .synchro/key and will not be shown again");
        }
        else
        {
            Console.WriteLine("peer key adopted from --key");
        }

        if (value.ExcludeLinesWritten.Count > 0)
        {
            Console.WriteLine($"hid the store from git status via .git/info/exclude ({string.Join(", ", value.ExcludeLinesWritten)})");
        }

        Console.WriteLine($"store ready: {StorePaths.FolderName}/");
        return 0;
    }

    private static bool TryParseOptions(
        string[] args,
        out string? keyHex,
        out string peer,
        out string? channel,
        out string? error)
    {
        keyHex = null;
        peer = "peer";
        channel = null;
        error = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--key" when i + 1 < args.Length:
                    keyHex = args[++i];
                    break;
                case var a when a.StartsWith("--key="):
                    keyHex = a["--key=".Length..];
                    break;
                case "--peer" when i + 1 < args.Length:
                    peer = args[++i];
                    break;
                case var a when a.StartsWith("--peer="):
                    peer = a["--peer=".Length..];
                    break;
                case "--channel" when i + 1 < args.Length:
                    channel = args[++i];
                    break;
                case var a when a.StartsWith("--channel="):
                    channel = a["--channel=".Length..];
                    break;
                case "--key" or "--peer" or "--channel":
                    error = $"missing value for '{arg}'";
                    return false;
                default:
                    error = $"unknown option '{arg}'";
                    return false;
            }
        }

        return true;
    }
}
