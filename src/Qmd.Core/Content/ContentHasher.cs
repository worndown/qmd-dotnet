using System.Security.Cryptography;
using System.Text;

namespace Qmd.Core.Content;

internal static class ContentHasher
{
    public static string HashContent(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
