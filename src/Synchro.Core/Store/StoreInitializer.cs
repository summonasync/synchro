using System.Security.Cryptography;

namespace Synchro.Core.Store;

public sealed record InitOptions
{
    public string? KeyHex { get; init; }
    public string Peer { get; init; } = "peer";
    public string? ChannelName { get; init; }
}

public sealed record InitOutcome
{
    public required SyncMode Mode { get; init; }
    public required string Channel { get; init; }
    public required string Peer { get; init; }
    public required bool KeyGenerated { get; init; }
    public required string? GeneratedKeyHex { get; init; }
    public required IReadOnlyList<string> ExcludeLinesWritten { get; init; }
}

public static class StoreInitializer
{
    private const int KeySize = 32;
    private const int KeyHexLength = KeySize * 2;

    public static Result<InitOutcome> Initialize(string root, InitOptions options)
    {
        var folder = StorePaths.Folder(root);
        if (Directory.Exists(folder) || File.Exists(folder))
        {
            return AlreadyInitialized();
        }

        var channel = options.ChannelName ?? new DirectoryInfo(root).Name;
        if (string.IsNullOrWhiteSpace(channel) || channel == Path.DirectorySeparatorChar.ToString())
        {
            return new Result<InitOutcome>.Fail(
                SyncError.Invalid("run init inside the project directory, not the filesystem root"));
        }

        byte[] key;
        bool generated;
        if (options.KeyHex is null)
        {
            key = RandomNumberGenerator.GetBytes(KeySize);
            generated = true;
        }
        else if (TryParseKeyHex(options.KeyHex, out var parsed))
        {
            key = parsed;
            generated = false;
        }
        else
        {
            return new Result<InitOutcome>.Fail(
                SyncError.Invalid($"--key must be {KeyHexLength} hex characters ({KeySize} bytes)"));
        }

        Directory.CreateDirectory(folder);

        try
        {
            using var keyStream = new FileStream(StorePaths.Key(root), KeyFileOptions());
            keyStream.Write(key);
        }
        catch (IOException)
        {
            return AlreadyInitialized();
        }

        var mode = Directory.Exists(Path.Combine(root, ".git")) ? SyncMode.Git : SyncMode.Plain;
        var state = new StoreState
        {
            SchemaVersion = 1,
            Channel = new ChannelConfig { Name = channel, Peer = options.Peer, Mode = mode },
            Ledger = [],
            AppliedFileHashes = [],
            AppliedContents = []
        };

        StoreContainer.Save(StorePaths.Container(root), key, state);

        var excluded = mode == SyncMode.Git ? GitHiding.EnsureExcluded(root) : [];

        return new Result<InitOutcome>.Ok(new InitOutcome
        {
            Mode = mode,
            Channel = channel,
            Peer = options.Peer,
            KeyGenerated = generated,
            GeneratedKeyHex = generated ? Convert.ToHexStringLower(key) : null,
            ExcludeLinesWritten = excluded
        });
    }

    public static bool TryParseKeyHex(string hex, out byte[] key)
    {
        key = [];
        if (hex.Length != KeyHexLength || !hex.All(Uri.IsHexDigit))
        {
            return false;
        }

        key = Convert.FromHexString(hex);
        return true;
    }

    public static bool TryLoadKey(string root, out byte[] key)
    {
        key = [];
        var keyPath = StorePaths.Key(root);
        if (!File.Exists(keyPath) || new FileInfo(keyPath).Length != KeySize)
        {
            return false;
        }

        key = File.ReadAllBytes(keyPath);
        return true;
    }

    private static Result<InitOutcome> AlreadyInitialized() =>
        new Result<InitOutcome>.Fail(SyncError.Conflict($"store already initialized at '{StorePaths.FolderName}'"));

    private static FileStreamOptions KeyFileOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }
}
