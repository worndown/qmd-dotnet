using Qmd.Core.Chunking;
using Qmd.Core.Database;
using Qmd.Core.Llm;
using Qmd.Core.Models;
using Qmd.Core.Paths;
using Qmd.Core.Retrieval;
using Qmd.Core.Snippets;

namespace Qmd.Core.Search;

/// <summary>
/// 8-step hybrid query pipeline combining BM25, vector search, RRF fusion, and LLM reranking.
/// </summary>
internal class HybridQueryService : IHybridQueryService
{
    private readonly IFtsSearchService _ftsSearch;
    private readonly IVectorSearchService _vectorSearch;
    private readonly IQueryExpanderService _queryExpander;
    private readonly IRerankerService _reranker;
    private readonly IQmdDatabase _db;
    private readonly ILlmService _llmService;
    private readonly SearchConfig _config;

    public HybridQueryService(
        IFtsSearchService ftsSearch,
        IVectorSearchService vectorSearch,
        IQueryExpanderService queryExpander,
        IRerankerService reranker,
        IQmdDatabase db,
        ILlmService llmService,
        SearchConfig searchConfig)
    {
        _ftsSearch = ftsSearch;
        _vectorSearch = vectorSearch;
        _queryExpander = queryExpander;
        _reranker = reranker;
        _db = db;
        _llmService = llmService;
        _config = searchConfig;
    }

    public async Task<List<HybridQueryResult>> HybridQueryAsync(
        string query,
        HybridQueryOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new HybridQueryOptions();
        var collections = options.Collections;
        var limit = options.Limit;
        var candidateLimit = options.CandidateLimit;
        var intent = options.Intent;

        // =====================================================================
        // Step 1: BM25 Probe — detect strong signal
        // =====================================================================
        var initialFts = _ftsSearch.Search(query, 20, collections);
        var topScore = initialFts.Count > 0 ? initialFts[0].Score : 0.0;
        var secondScore = initialFts.Count > 1 ? initialFts[1].Score : 0.0;
        var strongSignal = intent == null
            && topScore >= SearchConstants.StrongSignalMinScore
            && (topScore - secondScore) >= SearchConstants.StrongSignalMinGap;
        // When the caller's MinScore is below FtsMinSignal, relax the gate so
        // that valid-but-weak BM25 matches (e.g. single-word queries scoring 20-25%)
        // are included in RRF fusion instead of being silently discarded.
        var effectiveFtsMin = options.MinScore > 0
            ? Math.Min(options.MinScore, _config.FtsMinSignal)
            : _config.FtsMinSignal;
        var ftsWeak = topScore < effectiveFtsMin;
        if (options.Diagnostics != null && effectiveFtsMin < _config.FtsMinSignal)
            options.Diagnostics.RelaxedGates.Add($"FtsMinSignal ({_config.FtsMinSignal:F2} -> {effectiveFtsMin:F2})");

        // =====================================================================
        // Step 2: Query Expansion (skip if strong signal)
        // =====================================================================
        var expandedQueries = new List<ExpandedQuery>();
        if (!strongSignal)
        {
            expandedQueries = await _queryExpander.ExpandQueryAsync(query, null, intent, ct);
        }

        // =====================================================================
        // Step 3: Multi-backend search routing
        // =====================================================================
        var rankedLists = new List<List<RankedResult>>();
        var rankedListMeta = new List<RankedListMeta>();

        // 3a: Original FTS results as first list (only if non-empty to avoid wasting weight slots)
        if (initialFts.Count > 0 && !ftsWeak)
        {
            rankedLists.Add(initialFts.Select(r => new RankedResult(
                r.Filepath, r.DisplayPath, r.Title, r.Body ?? "", r.Score, r.Hash)).ToList());
            rankedListMeta.Add(new RankedListMeta("fts", "original", query));
        }

        // 3b: Expanded lex queries
        // When FTS lists are excluded, the positional weight rule (i < 2 -> 2.0)
        // shifts from FTS noise to the first 2 vector lists, amplifying vector signal.
        if (!ftsWeak)
        {
            foreach (var eq in expandedQueries.Where(q => q.Type == "lex"))
            {
                var ftsResults = _ftsSearch.Search(eq.Query, 20, collections);
                if (ftsResults.Count > 0)
                {
                    rankedLists.Add(ftsResults.Select(r => new RankedResult(
                        r.Filepath, r.DisplayPath, r.Title, r.Body ?? "", r.Score, r.Hash)).ToList());
                    rankedListMeta.Add(new RankedListMeta("fts", "lex", eq.Query));
                }
            }
        }

        // 3c: Vector search for original + vec/hyde queries
        var vecQueries = new List<(string Text, string Type)> { (query, "original") };
        vecQueries.AddRange(expandedQueries
            .Where(q => q.Type is "vec" or "hyde")
            .Select(q => (q.Query, q.Type)));

        // Check if vector table exists
        var vecTableExists = _db.Prepare(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='vectors_vec'").Get<SqliteMasterRow>();

        if (vecTableExists != null && _llmService != null)
        {
            // Batch embed all vector queries
            var textsToEmbed = vecQueries
                .Select(q => EmbeddingFormatter.FormatQueryForEmbedding(q.Text, _llmService.EmbedModelName))
                .ToList();
            var embeddings = await _llmService.EmbedBatchAsync(textsToEmbed, ct: ct);

            for (int i = 0; i < vecQueries.Count; i++)
            {
                if (embeddings[i] == null) continue;
                var vecResults = await _vectorSearch.SearchAsync(
                    vecQueries[i].Text, _llmService.EmbedModelName,
                    20, collections, embeddings[i]!.Embedding, ct);
                if (vecResults.Count > 0)
                {
                    rankedLists.Add(vecResults.Select(r => new RankedResult(
                        r.Filepath, r.DisplayPath, r.Title, r.Body ?? "", r.Score, r.Hash)).ToList());
                    rankedListMeta.Add(new RankedListMeta("vec", vecQueries[i].Type, vecQueries[i].Text));
                }
            }
        }

        // =====================================================================
        // Step 3d: Diagnostics + pre-reranking vector-score gate
        // =====================================================================
        var hasFts = rankedListMeta.Any(m => m.Source == "fts");
        var bestVecScore = 0.0;
        for (int li = 0; li < rankedLists.Count; li++)
        {
            if (rankedListMeta[li].Source == "vec" && rankedLists[li].Count > 0)
            {
                var listTopScore = rankedLists[li][0].Score;
                if (listTopScore > bestVecScore) bestVecScore = listTopScore;
            }
        }

        if (options.Diagnostics != null)
        {
            options.Diagnostics.HasFtsResults = hasFts;
            options.Diagnostics.BestVecScore = bestVecScore;
        }

        // Gate: when BM25 found nothing and all vector scores are below the noise
        // floor, there is no evidence of relevance — return empty early.
        // Relaxed by MinScore so that --min-score can override an aggressive
        // autotuned threshold that would otherwise silently discard results.
        var effectiveVecGate = options.MinScore > 0
            ? Math.Min(options.MinScore, _config.VecOnlyGateThreshold)
            : _config.VecOnlyGateThreshold;
        if (options.Diagnostics != null && effectiveVecGate < _config.VecOnlyGateThreshold)
            options.Diagnostics.RelaxedGates.Add($"VecOnlyGateThreshold ({_config.VecOnlyGateThreshold:F2} -> {effectiveVecGate:F2})");
        if (!hasFts && bestVecScore < effectiveVecGate && rankedLists.Count > 0)
            return [];

        // =====================================================================
        // Step 4: RRF Fusion
        // =====================================================================
        var weights = rankedLists.Select((_, i) => i < 2 ? 2.0 : 1.0).ToList();
        var fused = RrfFusion.Fuse(rankedLists, weights);
        var candidates = fused.Take(candidateLimit).ToList();

        // Build explain traces if requested
        Dictionary<string, RrfScoreTrace>? rrfTraces = null;
        if (options.Explain)
            rrfTraces = RrfFusion.BuildTrace(rankedLists, weights, rankedListMeta);

        // =====================================================================
        // Step 5: Chunk extraction with keyword overlap scoring
        // =====================================================================
        var queryTerms = query.ToLowerInvariant()
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
            var reranked = await _reranker.RerankAsync(query, chunksToRerank, null, intent, ct);
            rerankScores = reranked.ToDictionary(r => r.File, r => r.Score);
        }

        // =====================================================================
        // Step 6b: Reranker gate — if the best reranker score is near zero,
        // the reranker considers everything irrelevant.
        // Same MinScore relaxation as the other gates: lets the caller lower
        // the bar when they explicitly accept lower-confidence results.
        // =====================================================================
        if (rerankScores is { Count: > 0 })
        {
            var bestRerank = rerankScores.Values.Max();
            if (options.Diagnostics != null)
                options.Diagnostics.BestRerankScore = bestRerank;

            var effectiveRerankGate = options.MinScore > 0
                ? Math.Min(options.MinScore, _config.RerankGateThreshold)
                : _config.RerankGateThreshold;
            if (options.Diagnostics != null && effectiveRerankGate < _config.RerankGateThreshold)
                options.Diagnostics.RelaxedGates.Add($"RerankGateThreshold ({_config.RerankGateThreshold:F2} -> {effectiveRerankGate:F2})");
            if (!hasFts && bestRerank < effectiveRerankGate)
                return [];
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
        var results = blended
            .OrderByDescending(r => r.Score)
            .Where(r => { if (seen.Contains(r.File)) return false; seen.Add(r.File); return true; })
            .Where(r => r.Score >= options.MinScore)
            .Take(limit)
            .ToList();

        // =====================================================================
        // Step 9: Post-fusion confidence gap filter
        // Drop results that score below ConfidenceGapRatio of the top result,
        // keeping at least one result if any passed the min-score filter.
        // =====================================================================
        if (results.Count > 1)
        {
            var topBlended = results[0].Score;
            var floor = topBlended * _config.ConfidenceGapRatio;
            results = results.Where(r => r.Score >= floor).ToList();
        }

        return results;
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
