using Qmd.Core.Database;
using Qmd.Core.Embedding;
using Qmd.Core.Llm;
using Qmd.Core.Models;
using Qmd.Core.Paths;
using Qmd.Core.Retrieval;

namespace Qmd.Core.Search;

/// <summary>
/// Vector similarity search via sqlite-vec.
/// CRITICAL: Uses two-step query pattern. Single-query JOINs with vectors_vec hang indefinitely.
/// </summary>
public static class VectorSearcher
{
    public static async Task<List<SearchResult>> SearchVecAsync(
        IQmdDatabase db,
        string query,
        string model,
        ILlmService? llmService = null,
        float[]? precomputedEmbedding = null,
        int limit = 20,
        List<string>? collections = null,
        CancellationToken ct = default)
    {
        // Check if vectors_vec table exists
        var tableExists = db.Prepare("SELECT name FROM sqlite_master WHERE type='table' AND name='vectors_vec'")
            .GetDynamic();
        if (tableExists == null) return [];

        // Get embedding
        float[]? embedding = precomputedEmbedding;
        if (embedding == null && llmService != null)
        {
            var formatted = EmbeddingFormatter.FormatQueryForEmbedding(query, model);
            var result = await llmService.EmbedAsync(formatted, new EmbedOptions { Model = model, IsQuery = true }, ct);
            embedding = result?.Embedding;
        }
        if (embedding == null) return [];

        // STEP 1: Get vector matches from sqlite-vec (NO JOINs allowed!)
        var embeddingBytes = EmbeddingOperations.FloatArrayToBytes(embedding);
        var vecResults = db.Prepare("SELECT hash_seq, distance FROM vectors_vec WHERE embedding MATCH $1 AND k = $2")
            .AllDynamic(embeddingBytes, (long)(limit * 3));

        if (vecResults.Count == 0) return [];

        // Build distance map
        var distanceMap = new Dictionary<string, double>();
        var hashSeqs = new List<string>();
        foreach (var r in vecResults)
        {
            var hs = r["hash_seq"]!.ToString()!;
            hashSeqs.Add(hs);
            distanceMap[hs] = Convert.ToDouble(r["distance"]);
        }

        // STEP 2: Get document data (separate query — JOINs are safe here)
        var placeholders = string.Join(",", hashSeqs.Select((_, i) => $"${i + 1}"));
        var docSql = $@"
            SELECT
                cv.hash || '_' || cv.seq as hash_seq,
                cv.hash,
                cv.pos,
                'qmd://' || d.collection || '/' || d.path as filepath,
                d.collection || '/' || d.path as display_path,
                d.title,
                d.collection,
                content.doc as body
            FROM content_vectors cv
            JOIN documents d ON d.hash = cv.hash AND d.active = 1
            JOIN content ON content.hash = d.hash
            WHERE cv.hash || '_' || cv.seq IN ({placeholders})";

        var parameters = hashSeqs.Select(hs => (object?)hs).ToList();

        if (collections is { Count: > 0 })
        {
            var colPlaceholders = string.Join(",", collections.Select((_, i) => $"${parameters.Count + i + 1}"));
            docSql += $" AND d.collection IN ({colPlaceholders})";
            foreach (var c in collections)
                parameters.Add(c);
        }

        var docRows = db.Prepare(docSql).AllDynamic(parameters.ToArray());

        // Combine with distances and dedup by filepath (keep best distance)
        var seen = new Dictionary<string, (Dictionary<string, object?> Row, double BestDist)>();
        foreach (var row in docRows)
        {
            var hashSeq = row["hash_seq"]!.ToString()!;
            var filepath = row["filepath"]!.ToString()!;
            var distance = distanceMap.GetValueOrDefault(hashSeq, 1.0);

            if (!seen.TryGetValue(filepath, out var existing) || distance < existing.BestDist)
                seen[filepath] = (row, distance);
        }

        return seen.Values
            .OrderBy(x => x.BestDist)
            .Take(limit)
            .Select(x =>
            {
                var row = x.Row;
                var hash = row["hash"]!.ToString()!;
                var body = row["body"]?.ToString() ?? "";
                var virtualPath = row["filepath"]!.ToString()!;
                return new SearchResult
                {
                    Filepath = virtualPath,
                    DisplayPath = row["display_path"]!.ToString()!,
                    Title = row["title"]!.ToString()!,
                    Hash = hash,
                    DocId = DocidUtils.GetDocid(hash),
                    CollectionName = row["collection"]!.ToString()!,
                    ModifiedAt = "",
                    BodyLength = body.Length,
                    Body = body,
                    Score = 1 - x.BestDist,
                    Source = "vec",
                    ChunkPos = Convert.ToInt32(row["pos"] ?? 0),
                    Context = ContextResolver.GetContextForFile(db, virtualPath),
                };
            })
            .ToList();
    }
}
