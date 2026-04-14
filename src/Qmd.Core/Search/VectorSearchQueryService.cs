using Qmd.Core.Database;
using Qmd.Core.Llm;
using Qmd.Core.Models;
using Qmd.Core.Retrieval;
using Qmd.Core.Store;

namespace Qmd.Core.Search;

/// <summary>
/// Vector search with query expansion.
/// Expands query via LLM, filters to vec/hyde variants, runs vector search
/// for original + each expanded variant, deduplicates by filepath (best score).
/// </summary>
internal static class VectorSearchQueryService
{
    public static async Task<List<SearchResult>> SearchAsync(
        QmdStore store,
        ILlmService llmService,
        string query,
        VectorSearchQueryOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new VectorSearchQueryOptions();
        var limit = options.Limit;
        var minScore = options.MinScore;
        var collections = options.Collections;
        var intent = options.Intent;
        var model = llmService.EmbedModelName;

        // Check if vectors_vec table exists
        var vecTableExists = store.Db.Prepare(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='vectors_vec'").Get<SqliteMasterRow>();
        if (vecTableExists == null) return [];

        // Expand query — filter to vec/hyde only (lex queries target FTS, not vector)
        var allExpanded = await QueryExpander.ExpandQueryAsync(
            store.Db, llmService, query, null, intent, ct);
        var vecExpanded = allExpanded.Where(q => q.Type != "lex").ToList();

        // Run original + vec/hyde expanded through vector search sequentially
        // (concurrent embed() can cause issues)
        var queryTexts = new List<string> { query };
        queryTexts.AddRange(vecExpanded.Select(q => q.Query));

        var allResults = new Dictionary<string, SearchResult>();
        foreach (var q in queryTexts)
        {
            ct.ThrowIfCancellationRequested();
            var vecResults = await store.SearchVecAsync(q, model, limit, collections, ct: ct);
            foreach (var r in vecResults)
            {
                if (!allResults.TryGetValue(r.Filepath, out var existing) || r.Score > existing.Score)
                {
                    r.Context = ContextResolver.GetContextForFile(store.Db, r.Filepath);
                    allResults[r.Filepath] = r;
                }
            }
        }

        return allResults.Values
            .OrderByDescending(r => r.Score)
            .Where(r => r.Score >= minScore)
            .Take(limit)
            .ToList();
    }
}
