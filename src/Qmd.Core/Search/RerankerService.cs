using System.Security.Cryptography;
using System.Text;
using Qmd.Core.Database;
using Qmd.Core.Llm;
using Qmd.Core.Models;

namespace Qmd.Core.Search;

/// <summary>
/// Reranks documents using cross-encoder LLM with per-chunk caching.
/// </summary>
internal class RerankerService : IRerankerService
{
    private readonly IQmdDatabase db;
    private readonly ILlmService llmService;

    public RerankerService(IQmdDatabase db, ILlmService llmService)
    {
        this.db = db;
        this.llmService = llmService;
    }

    public async Task<List<(string File, double Score)>> RerankAsync(
        string query,
        List<RerankDocument> documents,
        string? model = null,
        string? intent = null,
        CancellationToken ct = default)
    {
        if (documents.Count == 0) return [];

        model ??= LlmConstants.DefaultRerankModel;
        var rerankQuery = intent != null ? $"{intent}\n\n{query}" : query;

        // Check cache for each document, keyed by chunk text (not file path)
        // so identical chunks from different files are scored once.
        var cachedResults = new Dictionary<string, double>(); // chunk text → score
        var uncachedDocsByChunk = new Dictionary<string, RerankDocument>(); // chunk text → doc (dedup)

        foreach (var doc in documents)
        {
            var cacheKey = ComputeCacheKey(rerankQuery, model, doc.Text);
            var cached = this.db.Prepare("SELECT result as value FROM llm_cache WHERE hash = $1").Get<SingleValueRow>(cacheKey);
            if (cached?.Value is string cachedScore && double.TryParse(cachedScore, out var score))
            {
                cachedResults[doc.Text] = score;
            }
            else
            {
                // Dedup by chunk text — identical chunks sent to reranker only once
                uncachedDocsByChunk.TryAdd(doc.Text, doc);
            }
        }

        // Batch rerank uncached documents
        if (uncachedDocsByChunk.Count > 0)
        {
            var uncachedDocs = uncachedDocsByChunk.Values.ToList();
            var rerankResult = await this.llmService.RerankAsync(rerankQuery, uncachedDocs,
                new RerankOptions { Model = model }, ct);

            foreach (var r in rerankResult.Results)
            {
                // Cache results by chunk text
                if (r.Index < uncachedDocs.Count)
                {
                    var chunkText = uncachedDocs[r.Index].Text;
                    var cacheKey = ComputeCacheKey(rerankQuery, model, chunkText);
                    var now = DateTime.UtcNow.ToString("o");
                    this.db.Prepare("INSERT OR REPLACE INTO llm_cache (hash, result, created_at) VALUES ($1, $2, $3)")
                        .Run(cacheKey, r.Score.ToString("G17"), now);
                    cachedResults[chunkText] = r.Score;
                }
            }
        }

        // Return results looked up by chunk text (not file path)
        return documents
            .Select(d => (d.File, cachedResults.GetValueOrDefault(d.Text, 0.0)))
            .OrderByDescending(x => x.Item2)
            .ToList();
    }

    private static string ComputeCacheKey(string query, string model, string text)
    {
        var input = $"{query}|{model}|{text}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
