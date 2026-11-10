using System.Security.Cryptography;

namespace Synchro.Core.Store;

public static class SealedBlob
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // shared container for store and packages: file bytes are nonce||ciphertext||tag,
    // deliberately free of magic headers so both stay indistinguishable from random data
    public static void Save(string path, byte[] key, byte[] aad, byte[] payload)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[payload.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, payload, ciphertext, tag, aad);
        }

        var tempPath = path + ".tmp";
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(nonce);
            fs.Write(ciphertext);
            fs.Write(tag);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    public static Result<byte[]> Load(string path, byte[] key, byte[] aad)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < NonceSize + TagSize)
            {
                return new Result<byte[]>.Fail(SyncError.Invalid("sealed blob is truncated"));
            }

            var nonce = bytes[..NonceSize];
            var tag = bytes[^TagSize..];
            var ciphertext = bytes[NonceSize..^TagSize];
            var payload = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, payload, aad);
            return new Result<byte[]>.Ok(payload);
        }
        catch (CryptographicException)
        {
            return new Result<byte[]>.Fail(SyncError.Invalid("wrong key or corrupted data"));
        }
        catch (IOException ex)
        {
            return new Result<byte[]>.Fail(SyncError.NotFound($"sealed blob unreadable: {ex.Message}"));
        }
        catch (UnauthorizedAccessException)
        {
            return new Result<byte[]>.Fail(SyncError.Invalid("sealed blob access denied"));
        }
    }

    public static byte[] Compress(string json)
    {
        using var output = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
        {
            deflate.Write(System.Text.Encoding.UTF8.GetBytes(json));
        }

        return output.ToArray();
    }

    public static string Decompress(byte[] payload)
    {
        using var source = new MemoryStream(payload);
        using var deflate = new System.IO.Compression.DeflateStream(source, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }
}
