using Qmd.Core.Database;
using Qmd.Core.Embedding;
using Qmd.Core.Llm;
using Qmd.Core.Models;
using Qmd.Core.Paths;
using Qmd.Core.Retrieval;

namespace Qmd.Core.Search;

/// <summary>
/// Vector similarity search via sqlite-vec.
/// TODO: Critical. Uses two-step query pattern. Single-query JOINs with vectors_vec hang indefinitely.
/// </summary>
internal class VectorSearchService : IVectorSearchService
{
    private readonly IQmdDatabase db;
    private readonly ILlmService? llmService;

    public VectorSearchService(IQmdDatabase db, ILlmService? llmService = null)
    {
        this.db = db;
        this.llmService = llmService;
    }

    public async Task<List<SearchResult>> SearchAsync(
        string query,
        string model,
        int limit = 20,
        List<string>? collections = null,
        float[]? precomputedEmbedding = null,
        CancellationToken ct = default)
    {
        // Check if vectors_vec table exists
        var tableExists = this.db.Prepare("SELECT name FROM sqlite_master WHERE type='table' AND name='vectors_vec'")
            .Get<SqliteMasterRow>();
        if (tableExists == null) return [];

        // Get embedding
        float[]? embedding = precomputedEmbedding;
        if (embedding == null && this.llmService != null)
        {
            var formatted = EmbeddingFormatter.FormatQueryForEmbedding(query, model);
            var result = await this.llmService.EmbedAsync(formatted, new EmbedOptions { Model = model, IsQuery = true }, ct);
            embedding = result?.Embedding;
        }
        if (embedding == null) return [];

        // STEP 1: Get vector matches from sqlite-vec (NO JOINs allowed!)
        var embeddingBytes = EmbeddingRepository.FloatArrayToBytes(embedding);
        var vecResults = this.db.Prepare("SELECT hash_seq, distance FROM vectors_vec WHERE embedding MATCH $1 AND k = $2")
            .All<VectorMatchRow>(embeddingBytes, (long)(limit * 3));

        if (vecResults.Count == 0) return [];

        // Build distance map
        var distanceMap = new Dictionary<string, double>();
        var hashSeqs = new List<string>();
        foreach (var r in vecResults)
        {
            hashSeqs.Add(r.HashSeq);
            distanceMap[r.HashSeq] = r.Distance;
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

        var docRows = this.db.Prepare(docSql).All<ContentVectorDocRow>(parameters.ToArray());

        // Combine with distances and dedup by filepath (keep best distance)
        var seen = new Dictionary<string, (ContentVectorDocRow Row, double BestDist)>();
        foreach (var row in docRows)
        {
            var distance = distanceMap.GetValueOrDefault(row.HashSeq, 1.0);

            if (!seen.TryGetValue(row.Filepath, out var existing) || distance < existing.BestDist)
                seen[row.Filepath] = (row, distance);
        }

        return seen.Values
            .OrderBy(x => x.BestDist)
            .Take(limit)
            .Select(x =>
            {
                var row = x.Row;
                var body = row.Body ?? "";
                var virtualPath = row.Filepath;
                return new SearchResult
                {
                    Filepath = virtualPath,
                    DisplayPath = row.DisplayPath,
                    Title = row.Title,
                    Hash = row.Hash,
                    DocId = DocIdUtils.GetDocId(row.Hash),
                    CollectionName = row.Collection,
                    ModifiedAt = "",
                    BodyLength = body.Length,
                    Body = body,
                    Score = 1 - x.BestDist,
                    Source = "vec",
                    ChunkPos = row.Pos,
                    Context = ContextResolver.GetContextForFile(this.db, virtualPath),
                };
            })
            .ToList();
    }
}
