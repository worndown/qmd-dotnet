using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Qmd.Core.Database;

namespace Qmd.Core.Indexing;

internal static class CacheOperations
{
    public static string GetCacheKey(string url, object body)
    {
        var combined = url + JsonSerializer.Serialize(body);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string? GetCachedResult(IQmdDatabase db, string cacheKey)
    {
        var row = db.Prepare("SELECT result as value FROM llm_cache WHERE hash = $1").Get<SingleValueRow>(cacheKey);
        return row?.Value;
    }

    public static void SetCachedResult(IQmdDatabase db, string cacheKey, string result)
    {
        var now = DateTime.UtcNow.ToString("o");
        db.Prepare("INSERT OR REPLACE INTO llm_cache (hash, result, created_at) VALUES ($1, $2, $3)")
            .Run(cacheKey, result, now);

        // 1% random cleanup — prevent unbounded cache growth
        if (Random.Shared.Next(100) == 0)
        {
            db.Prepare(@"
                DELETE FROM llm_cache WHERE hash NOT IN (
                    SELECT hash FROM llm_cache ORDER BY created_at DESC LIMIT 1000
                )
            ").Run();
        }
    }

    public static void ClearCache(IQmdDatabase db)
    {
        db.Prepare("DELETE FROM llm_cache").Run();
    }
}
