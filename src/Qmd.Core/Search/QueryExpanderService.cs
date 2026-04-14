using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Qmd.Core.Database;
using Qmd.Core.Llm;
using Qmd.Core.Models;

namespace Qmd.Core.Search;

/// <summary>
/// Expands search queries via LLM with SQLite caching.
/// Produces lex/vec/hyde query variants.
/// </summary>
internal class QueryExpanderService : IQueryExpanderService
{
    private readonly IQmdDatabase _db;
    private readonly ILlmService _llmService;

    public QueryExpanderService(IQmdDatabase db, ILlmService llmService)
    {
        _db = db;
        _llmService = llmService;
    }

    public async Task<List<ExpandedQuery>> ExpandQueryAsync(
        string query,
        string? model = null,
        string? intent = null,
        CancellationToken ct = default)
    {
        model ??= LlmConstants.DefaultGenerateModel;

        // Check cache
        var cacheKey = ComputeCacheKey(query, model, intent);
        var cached = _db.Prepare("SELECT result as value FROM llm_cache WHERE hash = $1").Get<SingleValueRow>(cacheKey);
        if (cached?.Value is string cachedJson)
        {
            var parsed = ParseCachedResult(cachedJson, query);
            if (parsed != null) return parsed;
        }

        // Call LLM
        var expandOptions = new ExpandQueryOptions
        {
            Context = intent,
            IncludeLexical = true,
        };
        var results = await _llmService.ExpandQueryAsync(query, expandOptions, ct);

        // Filter duplicates of original query
        var filtered = results
            .Where(r => !string.Equals(r.Text, query, StringComparison.Ordinal))
            .Select(r => new ExpandedQuery(r.Type.ToString().ToLowerInvariant(), r.Text))
            .ToList();

        // Cache result (only when non-empty)
        if (filtered.Count > 0)
        {
            var json = JsonSerializer.Serialize(filtered.Select(r => new { type = r.Type, query = r.Query }));
            var now = DateTime.UtcNow.ToString("o");
            _db.Prepare("INSERT OR REPLACE INTO llm_cache (hash, result, created_at) VALUES ($1, $2, $3)")
                .Run(cacheKey, json, now);
        }

        return filtered;
    }

    private static string ComputeCacheKey(string query, string model, string? intent)
    {
        var input = $"{query}|{model}|{intent ?? ""}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static List<ExpandedQuery>? ParseCachedResult(string json, string originalQuery)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var results = new List<ExpandedQuery>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;
                var queryText = item.TryGetProperty("query", out var q) ? q.GetString()
                    : item.TryGetProperty("text", out var txt) ? txt.GetString() : null; // legacy format
                if (type != null && queryText != null)
                    results.Add(new ExpandedQuery(type, queryText));
            }
            return results.Count > 0 ? results : null;
        }
        catch (JsonException) { return null; }
    }
}
