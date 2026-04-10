using Qmd.Core.Database;
using Qmd.Core.Models;

namespace Qmd.Core.Embedding;

/// <summary>
/// Database operations for embeddings: insert, query, clear.
/// </summary>
public static class EmbeddingOperations
{
    /// <summary>
    /// Get all unique content hashes that need embeddings (active docs without vectors).
    /// </summary>
    public static List<PendingEmbeddingDoc> GetPendingEmbeddingDocs(IQmdDatabase db)
    {
        var rows = db.Prepare(@"
            SELECT d.hash, MIN(d.path) as path, length(CAST(c.doc AS BLOB)) as bytes
            FROM documents d
            JOIN content c ON d.hash = c.hash
            LEFT JOIN content_vectors v ON d.hash = v.hash AND v.seq = 0
            WHERE d.active = 1 AND v.hash IS NULL
            GROUP BY d.hash
            ORDER BY MIN(d.path)
        ").AllDynamic();

        return rows.Select(r => new PendingEmbeddingDoc(
            r["hash"]!.ToString()!,
            r["path"]!.ToString()!,
            Convert.ToInt64(r["bytes"] ?? 0)
        )).ToList();
    }

    /// <summary>
    /// Load content bodies for a batch of pending docs.
    /// </summary>
    public static List<EmbeddingDoc> GetEmbeddingDocsForBatch(IQmdDatabase db, List<PendingEmbeddingDoc> batch)
    {
        if (batch.Count == 0) return [];

        var placeholders = string.Join(",", batch.Select((_, i) => $"${i + 1}"));
        var rows = db.Prepare($"SELECT hash, doc as body FROM content WHERE hash IN ({placeholders})")
            .AllDynamic(batch.Select(d => (object?)d.Hash).ToArray());

        var bodyByHash = new Dictionary<string, string>();
        foreach (var r in rows)
            bodyByHash[r["hash"]!.ToString()!] = r["body"]?.ToString() ?? "";

        return batch.Select(d => new EmbeddingDoc(
            d.Hash, d.Path, d.Bytes,
            bodyByHash.GetValueOrDefault(d.Hash, "")
        )).ToList();
    }

    /// <summary>
    /// Insert a single embedding. Crash-safe ordering: content_vectors first, then vectors_vec.
    /// </summary>
    // Cache whether vectors_vec table exists to avoid per-insert schema query
    private static bool? _vecTableExists;

    public static void InsertEmbedding(IQmdDatabase db, string hash, int seq, int pos,
        float[] embedding, string model, string embeddedAt)
    {
        var hashSeq = $"{hash}_{seq}";

        // 1. Insert metadata first (crash-safe)
        db.Prepare("INSERT OR REPLACE INTO content_vectors (hash, seq, pos, model, embedded_at) VALUES ($1, $2, $3, $4, $5)")
            .Run(hash, (long)seq, (long)pos, model, embeddedAt);

        // 2. vec0 doesn't support OR REPLACE — use DELETE + INSERT
        _vecTableExists ??= db.Prepare("SELECT name FROM sqlite_master WHERE type='table' AND name='vectors_vec'").GetDynamic() != null;
        if (_vecTableExists.Value)
        {
            db.Prepare("DELETE FROM vectors_vec WHERE hash_seq = $1").Run(hashSeq);
            var bytes = FloatArrayToBytes(embedding);
            db.Prepare("INSERT INTO vectors_vec (hash_seq, embedding) VALUES ($1, $2)")
                .Run(hashSeq, bytes);
        }
    }

    /// <summary>Reset vec table cache — needed after table creation during embedding.</summary>
    public static void ResetVecTableCache() => _vecTableExists = null;

    /// <summary>
    /// Clear all embeddings (force re-embed).
    /// </summary>
    public static void ClearAllEmbeddings(IQmdDatabase db)
    {
        db.Exec("DELETE FROM content_vectors");
        db.Exec("DROP TABLE IF EXISTS vectors_vec");
    }

    /// <summary>
    /// Get hashes that need embedding (simplified query for status).
    /// </summary>
    public static List<(string Hash, string Body, string Path)> GetHashesForEmbedding(IQmdDatabase db)
    {
        var rows = db.Prepare(@"
            SELECT d.hash, c.doc as body, MIN(d.path) as path
            FROM documents d
            JOIN content c ON d.hash = c.hash
            LEFT JOIN content_vectors v ON d.hash = v.hash AND v.seq = 0
            WHERE d.active = 1 AND v.hash IS NULL
            GROUP BY d.hash
        ").AllDynamic();

        return rows.Select(r => (
            r["hash"]!.ToString()!,
            r["body"]?.ToString() ?? "",
            r["path"]!.ToString()!
        )).ToList();
    }

    /// <summary>
    /// Convert float[] to byte[] for sqlite-vec BLOB parameters.
    /// Both .NET and TypeScript Float32Array use IEEE 754 little-endian on x64.
    /// </summary>
    public static byte[] FloatArrayToBytes(float[] floats)
    {
        var bytes = new byte[floats.Length * sizeof(float)];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
