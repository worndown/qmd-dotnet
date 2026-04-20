using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Qmd.Core.Database;

namespace Qmd.Core.Indexing;

internal class CacheRepository : ICacheRepository
{
    private readonly IQmdDatabase db;

    public CacheRepository(IQmdDatabase db)
    {
        this.db = db;
    }

    public static string GetCacheKey(string url, object body)
    {
        var combined = url + JsonSerializer.Serialize(body);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string? GetCachedResult(string cacheKey)
    {
        var row = this.db.Prepare("SELECT result as value FROM llm_cache WHERE hash = $1").Get<SingleValueRow>(cacheKey);
        return row?.Value;
    }

    public void SetCachedResult(string cacheKey, string result)
    {
        var now = DateTime.UtcNow.ToString("o");
        this.db.Prepare("INSERT OR REPLACE INTO llm_cache (hash, result, created_at) VALUES ($1, $2, $3)")
            .Run(cacheKey, result, now);

        // 1% random cleanup — prevent unbounded cache growth
        if (Random.Shared.Next(100) == 0)
        {
            this.db.Prepare(@"
                DELETE FROM llm_cache WHERE hash NOT IN (
                    SELECT hash FROM llm_cache ORDER BY created_at DESC LIMIT 1000
                )
            ").Run();
        }
    }

    public void ClearCache()
    {
        this.db.Prepare("DELETE FROM llm_cache").Run();
    }
}
