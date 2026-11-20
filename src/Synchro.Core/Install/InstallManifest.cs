using System.Text.Json;

namespace Synchro.Core.Install;

public static class InstallManifest
{
    private const string FileName = "install.json";

    private sealed record ManifestData(string Version);

    public static string PathFor(string root) => Path.Combine(root, FileName);

    public static Result<string?> Read(string root)
    {
        var path = PathFor(root);
        if (!File.Exists(path))
        {
            return new Result<string?>.Ok(null);
        }

        try
        {
            var data = JsonSerializer.Deserialize<ManifestData>(File.ReadAllText(path));
            return data?.Version is { Length: > 0 } version
                ? new Result<string?>.Ok(version)
                : new Result<string?>.Fail(SyncError.Invalid($"install manifest at '{path}' carries no version"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Result<string?>.Fail(SyncError.Invalid($"install manifest at '{path}' is unreadable: {ex.Message}"));
        }
    }

    public static Result<string> Write(string root, string version)
    {
        try
        {
            Directory.CreateDirectory(root);
            var path = PathFor(root);
            File.WriteAllText(path, JsonSerializer.Serialize(new ManifestData(version)));
            return new Result<string>.Ok(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<string>.Fail(SyncError.Conflict($"install manifest could not be written under '{root}': {ex.Message}"));
        }
    }
}
