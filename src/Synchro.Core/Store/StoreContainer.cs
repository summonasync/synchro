using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Synchro.Core.Store;

public static class StoreContainer
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Aad = "synchro-store-v1"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new();

    public static void Save(string containerPath, byte[] key, StoreState state)
    {
        var payload = Compress(JsonSerializer.Serialize(state, JsonOptions));
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[payload.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, payload, ciphertext, tag, Aad);

        using var fs = File.Create(containerPath);
        fs.Write(nonce);
        fs.Write(ciphertext);
        fs.Write(tag);
    }

    public static Result<StoreState> Load(string containerPath, byte[] key)
    {
        try
        {
            var bytes = File.ReadAllBytes(containerPath);
            if (bytes.Length < NonceSize + TagSize)
            {
                return new Result<StoreState>.Fail(SyncError.Invalid("store container is truncated"));
            }

            var nonce = bytes[..NonceSize];
            var tag = bytes[^TagSize..];
            var ciphertext = bytes[NonceSize..^TagSize];
            var payload = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, payload, Aad);

            var json = Decompress(payload);
            var state = JsonSerializer.Deserialize<StoreState>(json, JsonOptions);
            return state is null
                ? new Result<StoreState>.Fail(SyncError.Invalid("store container holds no state"))
                : new Result<StoreState>.Ok(state);
        }
        catch (CryptographicException)
        {
            return new Result<StoreState>.Fail(SyncError.Invalid("wrong key or corrupted container"));
        }
        catch (JsonException)
        {
            return new Result<StoreState>.Fail(SyncError.Invalid("store container state is unreadable"));
        }
        catch (IOException ex)
        {
            return new Result<StoreState>.Fail(SyncError.NotFound($"store container unreadable: {ex.Message}"));
        }
    }

    private static byte[] Compress(string json)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
        {
            deflate.Write(Encoding.UTF8.GetBytes(json));
        }

        return output.ToArray();
    }

    private static string Decompress(byte[] payload)
    {
        using var source = new MemoryStream(payload);
        using var deflate = new DeflateStream(source, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
