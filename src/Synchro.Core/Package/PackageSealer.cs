using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Synchro.Core;
using Synchro.Core.Store;

namespace Synchro.Core.Package;

public static class PackageSealer
{
    private static readonly byte[] Aad = "synchro-package-v1"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new();

    public static void Seal(string path, byte[] key, SyncManifest manifest)
    {
        var payload = SealedBlob.Compress(JsonSerializer.Serialize(manifest, JsonOptions));
        SealedBlob.Save(path, key, Aad, payload);
    }

    public static Result<SyncManifest> Open(string path, byte[] key)
    {
        var payload = SealedBlob.Load(path, key, Aad);
        if (payload is not Result<byte[]>.Ok ok)
        {
            return new Result<SyncManifest>.Fail(((Result<byte[]>.Fail)payload).Error);
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<SyncManifest>(SealedBlob.Decompress(ok.Value), JsonOptions);
            return manifest is null
                ? new Result<SyncManifest>.Fail(SyncError.Invalid("package holds no manifest"))
                : new Result<SyncManifest>.Ok(manifest);
        }
        catch (JsonException)
        {
            return new Result<SyncManifest>.Fail(SyncError.Invalid("package manifest is unreadable"));
        }
        catch (InvalidDataException)
        {
            return new Result<SyncManifest>.Fail(SyncError.Invalid("package payload is malformed"));
        }
    }
}
