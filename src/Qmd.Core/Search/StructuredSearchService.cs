using Qmd.Core.Chunking;
using Qmd.Core.Database;
using Qmd.Core.Llm;
using Qmd.Core.Models;
using Qmd.Core.Paths;
using Qmd.Core.Retrieval;
using Qmd.Core.Snippets;

namespace Qmd.Core.Search;

/// <summary>
/// Structured search: takes pre-expanded queries (lex/vec/hyde) and runs them
/// through FTS + vector search, RRF fusion, chunk selection, and reranking.
/// Unlike HybridQueryService, this skips query expansion — the caller provides expansions.
/// </summary>
internal class StructuredSearchService : IStructuredSearchService
{
    private readonly IFtsSearchService _ftsSearch;
    private readonly IVectorSearchService _vectorSearch;
    private readonly IRerankerService _reranker;
    private readonly IQmdDatabase _db;
    private readonly ILlmService _llmService;

    public StructuredSearchService(
        IFtsSearchService ftsSearch,
        IVectorSearchService vectorSearch,
        IRerankerService reranker,
        IQmdDatabase db,
        ILlmService llmService)
    {
        _ftsSearch = ftsSearch;
        _vectorSearch = vectorSearch;
        _reranker = reranker;
        _db = db;
        _llmService = llmService;
    }

    public async Task<List<HybridQueryResult>> SearchAsync(
        List<ExpandedQuery> searches,
        StructuredSearchOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new StructuredSearchOptions();
        var collections = options.Collections;
        var limit = options.Limit;
        var candidateLimit = options.CandidateLimit;
        var intent = options.Intent;

        // =====================================================================
        // Step 1: Validate queries
        // =====================================================================
        foreach (var search in searches)
        {
            var location = search.Line.HasValue ? $"Line {search.Line}" : "Structured search";

            if (search.Query.Contains('\r') || search.Query.Contains('\n'))
                throw new ArgumentException($"{location} ({search.Type}): queries must be single-line");

            if (search.Type == "lex")
            {
                var error = QueryValidator.ValidateLexQuery(search.Query);
                if (error != null) throw new ArgumentException($"{location} (lex): {error}");
            }
            else if (search.Type is "vec" or "hyde")
            {
                var error = QueryValidator.ValidateSemanticQuery(search.Query);
                if (error != null) throw new ArgumentException($"{location} ({search.Type}): {error}");
            }
        }

        // =====================================================================
        // Step 2: FTS search for lex queries
        // =====================================================================
        var rankedLists = new List<List<RankedResult>>();
        var rankedListMeta = new List<RankedListMeta>();
        // undefined collection = search all; otherwise iterate per collection
        var collectionList = collections is { Count: > 0 }
            ? collections.Select(c => (List<string>?)[c]).ToList()
            : new List<List<string>?> { null };

        foreach (var search in searches.Where(s => s.Type == "lex"))
        {
            foreach (var coll in collectionList)
            {
                var ftsResults = _ftsSearch.Search(search.Query, 20, coll);
                if (ftsResults.Count > 0)
                {
                    rankedLists.Add(ftsResults.Select(r => new RankedResult(
                        r.Filepath, r.DisplayPath, r.Title, r.Body ?? "", r.Score, r.Hash)).ToList());
                    rankedListMeta.Add(new RankedListMeta("fts", "lex", search.Query));
                }
            }
        }

        // =====================================================================
        // Step 3: Vector search for vec/hyde queries
        // =====================================================================
        var vecSearches = searches.Where(s => s.Type is "vec" or "hyde").ToList();

        var vecTableExists = _db.Prepare(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='vectors_vec'").Get<SqliteMasterRow>();

        if (vecTableExists != null && _llmService != null && vecSearches.Count > 0)
        {
            // Batch embed all vec/hyde queries
            var textsToEmbed = vecSearches
                .Select(s => EmbeddingFormatter.FormatQueryForEmbedding(s.Query, _llmService.EmbedModelName))
                .ToList();
            var embeddings = await _llmService.EmbedBatchAsync(textsToEmbed, ct: ct);

            for (int i = 0; i < vecSearches.Count; i++)
            {
                if (embeddings[i] == null) continue;
                foreach (var coll in collectionList)
                {
                    var vecResults = await _vectorSearch.SearchAsync(
                        vecSearches[i].Query, _llmService.EmbedModelName,
                        20, coll, embeddings[i]!.Embedding, ct);
                    if (vecResults.Count > 0)
                    {
                        rankedLists.Add(vecResults.Select(r => new RankedResult(
                            r.Filepath, r.DisplayPath, r.Title, r.Body ?? "", r.Score, r.Hash)).ToList());
                        rankedListMeta.Add(new RankedListMeta("vec", vecSearches[i].Type, vecSearches[i].Query));
                    }
                }
            }
        }

        if (rankedLists.Count == 0)
            return [];

        // =====================================================================
        // Step 4: RRF Fusion (first list gets 2x weight)
        // =====================================================================
        var weights = rankedLists.Select((_, i) => i == 0 ? 2.0 : 1.0).ToList();
        var fused = RrfFusion.Fuse(rankedLists, weights);
        var candidates = fused.Take(candidateLimit).ToList();

        Dictionary<string, RrfScoreTrace>? rrfTraces = null;
        if (options.Explain)
            rrfTraces = RrfFusion.BuildTrace(rankedLists, weights, rankedListMeta);

        // =====================================================================
        // Step 5: Chunk selection with keyword matching
        // =====================================================================
        // Determine primary query (prefer lex, fallback to vec)
        var primaryQuery = searches.FirstOrDefault(s => s.Type == "lex")?.Query
            ?? searches.FirstOrDefault(s => s.Type is "vec" or "hyde")?.Query
            ?? searches[0].Query;

        var queryTerms = primaryQuery.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2).ToArray();
        var intentTerms = intent != null ? IntentProcessor.ExtractIntentTerms(intent) : [];

        var candidatesWithChunks = new List<(RankedResult Cand, string BestChunk, int BestChunkPos, int RrfRank)>();
        for (int rank = 0; rank < candidates.Count; rank++)
        {
            var cand = candidates[rank];
            var chunks = DocumentChunker.ChunkDocument(cand.Body, filepath: cand.File, strategy: options.ChunkStrategy);

            int bestIdx = 0;
            double bestScore = -1;
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunkLower = chunks[i].Text.ToLowerInvariant();
                double score = queryTerms.Count(term => chunkLower.Contains(term));
                score += intentTerms.Count(term => chunkLower.Contains(term)) * SnippetExtractor.IntentWeightChunk;
                if (score > bestScore) { bestScore = score; bestIdx = i; }
            }

            candidatesWithChunks.Add((cand, chunks[bestIdx].Text, chunks[bestIdx].Pos, rank + 1));
        }

        // =====================================================================
        // Step 6: Reranking (optional)
        // =====================================================================
        Dictionary<string, double>? rerankScores = null;
        if (!options.SkipRerank)
        {
            var chunksToRerank = candidatesWithChunks
                .Select(c => new RerankDocument(c.Cand.File, c.BestChunk))
                .ToList();
            var reranked = await _reranker.RerankAsync(primaryQuery, chunksToRerank, null, intent, ct);
            rerankScores = reranked.ToDictionary(r => r.File, r => r.Score);
        }

        // =====================================================================
        // Step 7: Position-aware score blending
        // =====================================================================
        var blended = new List<HybridQueryResult>();
        foreach (var (cand, bestChunk, bestChunkPos, rrfRank) in candidatesWithChunks)
        {
            double finalScore;
            double rerankScore = 0;

            if (rerankScores != null && rerankScores.TryGetValue(cand.File, out var rs))
            {
                rerankScore = rs;
                double rrfWeight = rrfRank <= 3 ? 0.75 : rrfRank <= 10 ? 0.60 : 0.40;
                double rrfScore = 1.0 / rrfRank;
                finalScore = rrfWeight * rrfScore + (1 - rrfWeight) * rerankScore;
            }
            else
            {
                finalScore = 1.0 / rrfRank; // No reranking — use position-based score
            }

            blended.Add(new HybridQueryResult
            {
                File = cand.File,
                DisplayPath = cand.DisplayPath,
                Title = cand.Title,
                Body = cand.Body,
                BestChunk = bestChunk,
                BestChunkPos = bestChunkPos,
                Score = finalScore,
                Context = ContextResolver.GetContextForFile(_db, cand.File),
                Docid = DocidUtils.GetDocid(cand.Hash),
                Explain = options.Explain ? BuildExplain(cand.File, rrfTraces, rerankScore, finalScore) : null,
            });
        }

        // =====================================================================
        // Step 8: Dedup, filter, sort, limit
        // =====================================================================
        var seen = new HashSet<string>();
        return blended
            .OrderByDescending(r => r.Score)
            .Where(r => { if (seen.Contains(r.File)) return false; seen.Add(r.File); return true; })
            .Where(r => r.Score >= options.MinScore)
            .Take(limit)
            .ToList();
    }

    private static HybridQueryExplain BuildExplain(string file,
        Dictionary<string, RrfScoreTrace>? rrfTraces, double rerankScore, double finalScore)
    {
        var trace = rrfTraces?.GetValueOrDefault(file);
        var ftsScores = new List<double>();
        var vecScores = new List<double>();
        if (trace?.Contributions != null)
        {
            foreach (var c in trace.Contributions)
            {
                if (c.Source == "fts") ftsScores.Add(c.BackendScore);
                else vecScores.Add(c.BackendScore);
            }
        }
        return new HybridQueryExplain(ftsScores, vecScores, trace, rerankScore, finalScore);
    }
}
