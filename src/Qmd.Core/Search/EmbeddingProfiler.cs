using Qmd.Core.Database;
using Qmd.Core.Embedding;
using Qmd.Core.Llm;
using Qmd.Core.Models;

namespace Qmd.Core.Search;

/// <summary>
/// Profiles the embedding model's similarity distribution on the indexed corpus.
/// Samples random document chunks, uses each as a query against the vector index,
/// and collects inter-document cosine similarity scores to characterize the noise floor.
/// </summary>
internal static class EmbeddingProfiler
{
    public static async Task<EmbeddingProfile> ProfileAsync(
        IQmdDatabase db,
        ILlmService llmService,
        EmbeddingProfileOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new EmbeddingProfileOptions();
        var sampleSize = options.SampleSize;
        var model = llmService.EmbedModelName;

        // Get total embedded chunk count and model dimensions
        var vecTableExists = db.Prepare(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='vectors_vec'").Get<SqliteMasterRow>();
        if (vecTableExists == null)
            throw new InvalidOperationException("No vector index found. Run 'qmd embed' first.");

        var countRow = db.Prepare("SELECT COUNT(*) as cnt FROM content_vectors WHERE model = $1")
            .Get<CountRow>(model);
        var totalChunks = countRow?.Cnt ?? 0;
        if (totalChunks < 2)
            throw new InvalidOperationException($"Need at least 2 embedded chunks to profile (found {totalChunks}).");

        // Sample random chunks — get their text content for re-embedding as queries
        var actualSample = Math.Min(sampleSize, totalChunks);
        var collectionFilter = options.Collections is { Count: > 0 }
            ? "AND d.collection IN (" + string.Join(",", options.Collections.Select((_, i) => $"${i + 2}")) + ")"
            : "";

        var sampleSql = $@"
            SELECT cv.hash, cv.seq, cv.pos, d.collection,
                   substr(content.doc, cv.pos + 1, 900) as chunk_text
            FROM content_vectors cv
            JOIN documents d ON d.hash = cv.hash AND d.active = 1
            JOIN content ON content.hash = cv.hash
            WHERE cv.model = $1 {collectionFilter}
            ORDER BY RANDOM()
            LIMIT {actualSample}";

        var parameters = new List<object?> { model };
        if (options.Collections is { Count: > 0 })
            parameters.AddRange(options.Collections.Select(c => (object?)c));

        var sampleRows = db.Prepare(sampleSql).All<ChunkSampleRow>(parameters.ToArray());
        if (sampleRows.Count < 2)
            throw new InvalidOperationException($"Only {sampleRows.Count} chunks matched filters. Need at least 2.");

        // Embed each sampled chunk as a query, then search for neighbors
        var allScores = new List<double>();
        foreach (var row in sampleRows)
        {
            ct.ThrowIfCancellationRequested();

            var chunkText = row.ChunkText ?? "";
            if (string.IsNullOrWhiteSpace(chunkText)) continue;

            var formatted = EmbeddingFormatter.FormatQueryForEmbedding(chunkText, model);
            var embedResult = await llmService.EmbedAsync(formatted,
                new EmbedOptions { Model = model, IsQuery = true }, ct);
            if (embedResult?.Embedding == null) continue;

            // Search the vector index — get top-k neighbors
            var embeddingBytes = EmbeddingOperations.FloatArrayToBytes(embedResult.Embedding);
            var vecResults = db.Prepare(
                "SELECT hash_seq, distance FROM vectors_vec WHERE embedding MATCH $1 AND k = $2")
                .All<VectorMatchRow>(embeddingBytes, 11L); // top 11 so we can skip self-match

            var selfHash = row.Hash + "_" + row.Seq;
            foreach (var vr in vecResults)
            {
                if (vr.HashSeq == selfHash) continue; // skip self

                var score = 1.0 - vr.Distance;
                allScores.Add(score);
            }
        }

        if (allScores.Count == 0)
            throw new InvalidOperationException("No inter-document similarity scores collected.");

        allScores.Sort();

        // Get model dimensions from vector table schema
        var dimRow = db.Prepare(
            "SELECT sql as value FROM sqlite_master WHERE type='table' AND name='vectors_vec'").Get<SingleValueRow>();
        var dimStr = dimRow?.Value ?? "";
        var dimensions = 0;
        var floatIdx = dimStr.IndexOf("float[", StringComparison.Ordinal);
        if (floatIdx >= 0)
        {
            var start = floatIdx + 6;
            var end = dimStr.IndexOf(']', start);
            if (end > start) int.TryParse(dimStr[start..end], out dimensions);
        }

        return new EmbeddingProfile
        {
            Model = model,
            Dimensions = dimensions,
            TotalChunks = totalChunks,
            SampleSize = sampleRows.Count,
            ScoreCount = allScores.Count,
            Min = allScores[0],
            Max = allScores[^1],
            Mean = allScores.Average(),
            Median = Percentile(allScores, 0.50),
            P5 = Percentile(allScores, 0.05),
            P25 = Percentile(allScores, 0.25),
            P75 = Percentile(allScores, 0.75),
            P95 = Percentile(allScores, 0.95),
            SuggestedVsearchMinScore = Percentile(allScores, 0.75),
        };
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var index = p * (sorted.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        var frac = index - lower;
        return sorted[lower] * (1 - frac) + sorted[upper] * frac;
    }
}
