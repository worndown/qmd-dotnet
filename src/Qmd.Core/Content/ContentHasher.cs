using System.Security.Cryptography;
using System.Text;
using Qmd.Core.Database;

namespace Qmd.Core.Content;

internal static class ContentHasher
{
    public static string HashContent(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // Phase 5: this database operation will move to a repository service.
    public static void InsertContent(IQmdDatabase db, string hash, string content, string createdAt)
    {
        db.Prepare("INSERT OR IGNORE INTO content (hash, doc, created_at) VALUES ($1, $2, $3)")
            .Run(hash, content, createdAt);
    }
}
