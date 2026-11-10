using System.Security.Cryptography;

namespace Synchro.Core.Hashing;

public static class ContentHash
{
    public static string Compute(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(Normalize(content)));

    public static byte[] Normalize(byte[] content)
    {
        var hasCr = Array.IndexOf(content, (byte)'\r') >= 0;
        if (!hasCr)
        {
            return content;
        }

        var output = new List<byte>(content.Length);
        for (var i = 0; i < content.Length; i++)
        {
            var b = content[i];
            if (b == (byte)'\r' && i + 1 < content.Length && content[i + 1] == (byte)'\n')
            {
                continue;
            }

            output.Add(b);
        }

        return [.. output];
    }
}
