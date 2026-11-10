using System.Text.Json;

namespace Synchro.Core.Store;

public static class StoreContainer
{
    private const int SchemaVersion = 1;
    private static readonly byte[] Aad = "synchro-store-v1"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new();

    public static void Save(string containerPath, byte[] key, StoreState state)
    {
        var payload = SealedBlob.Compress(JsonSerializer.Serialize(state, JsonOptions));
        SealedBlob.Save(containerPath, key, Aad, payload);
    }

    public static Result<StoreState> Load(string containerPath, byte[] key)
    {
        var payload = SealedBlob.Load(containerPath, key, Aad);
        if (payload is not Result<byte[]>.Ok ok)
        {
            return new Result<StoreState>.Fail(((Result<byte[]>.Fail)payload).Error);
        }

        try
        {
            var state = JsonSerializer.Deserialize<StoreState>(SealedBlob.Decompress(ok.Value), JsonOptions);
            if (state is null)
            {
                return new Result<StoreState>.Fail(SyncError.Invalid("store container holds no state"));
            }

            if (state.SchemaVersion != SchemaVersion)
            {
                return new Result<StoreState>.Fail(
                    SyncError.Invalid($"unsupported store schema version {state.SchemaVersion}"));
            }

            return new Result<StoreState>.Ok(state);
        }
        catch (JsonException)
        {
            return new Result<StoreState>.Fail(SyncError.Invalid("store container state is unreadable"));
        }
    }
}
