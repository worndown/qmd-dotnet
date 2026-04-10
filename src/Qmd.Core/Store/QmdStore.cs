using Qmd.Core.Chunking;
using Qmd.Core.Configuration;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Embedding;
using Qmd.Core.Indexing;
using Qmd.Core.Llm;
using Qmd.Core.Models;
using Qmd.Core.Paths;
using Qmd.Core.Retrieval;
using Qmd.Core.Search;

namespace Qmd.Core.Store;

/// <summary>
/// Central facade connecting all QMD subsystems.
/// Mirrors the TypeScript Store type from store.ts.
/// </summary>
public class QmdStore : IDisposable
{
    public IQmdDatabase Db { get; }
    public string DbPath { get; }
    public ITokenizer Tokenizer { get; set; } = new CharBasedTokenizer();

    public QmdStore(string? dbPath = null)
    {
        DbPath = dbPath ?? QmdPaths.GetDefaultDbPath();
        Db = new SqliteDatabase(DbPath);
        SchemaInitializer.Initialize(Db);
        VecExtension.TryLoad(Db);
    }

    /// <summary>Create store with a pre-opened database (for testing with :memory:).</summary>
    public QmdStore(IQmdDatabase db, string dbPath = ":memory:")
    {
        Db = db;
        DbPath = dbPath;
        SchemaInitializer.Initialize(Db);
        VecExtension.TryLoad(Db);
    }

    // =========================================================================
    // Content
    // =========================================================================
    public string HashContent(string content) => ContentHasher.HashContent(content);
    public void InsertContent(string hash, string content, string createdAt) =>
        ContentHasher.InsertContent(Db, hash, content, createdAt);
    public string ExtractTitle(string content, string filename) =>
        TitleExtractor.ExtractTitle(content, filename);

    // =========================================================================
    // Documents
    // =========================================================================
    public void InsertDocument(string collection, string path, string title, string hash,
        string createdAt, string modifiedAt) =>
        DocumentOperations.InsertDocument(Db, collection, path, title, hash, createdAt, modifiedAt);

    public ActiveDocumentRow? FindActiveDocument(string collection, string path) =>
        DocumentOperations.FindActiveDocument(Db, collection, path);

    public void UpdateDocumentTitle(long id, string title, string modifiedAt) =>
        DocumentOperations.UpdateDocumentTitle(Db, id, title, modifiedAt);

    public void UpdateDocument(long id, string title, string hash, string modifiedAt) =>
        DocumentOperations.UpdateDocument(Db, id, title, hash, modifiedAt);

    public void DeactivateDocument(string collection, string path) =>
        DocumentOperations.DeactivateDocument(Db, collection, path);

    public List<string> GetActiveDocumentPaths(string collection) =>
        DocumentOperations.GetActiveDocumentPaths(Db, collection);

    // =========================================================================
    // Search
    // =========================================================================
    public List<SearchResult> SearchFTS(string query, int limit = 20, List<string>? collections = null) =>
        FtsSearcher.SearchFTS(Db, query, limit, collections);

    // =========================================================================
    // Chunking
    // =========================================================================
    public List<TextChunk> ChunkDocument(string content, int maxChars = ChunkConstants.ChunkSizeChars,
        int overlapChars = ChunkConstants.ChunkOverlapChars) =>
        DocumentChunker.ChunkDocument(content, maxChars, overlapChars);

    public List<TextChunk> ChunkDocument(string content, string? filepath,
        ChunkStrategy strategy = ChunkStrategy.Regex,
        int maxChars = ChunkConstants.ChunkSizeChars,
        int overlapChars = ChunkConstants.ChunkOverlapChars) =>
        DocumentChunker.ChunkDocument(content, filepath, strategy, maxChars, overlapChars);

    public List<TokenizedChunk> ChunkDocumentByTokens(
        string content,
        int maxTokens = ChunkConstants.ChunkSizeTokens,
        int overlapTokens = ChunkConstants.ChunkOverlapTokens,
        int windowTokens = ChunkConstants.ChunkWindowTokens,
        string? filepath = null,
        ChunkStrategy chunkStrategy = ChunkStrategy.Regex,
        CancellationToken cancellationToken = default) =>
        DocumentChunker.ChunkDocumentByTokens(
            Tokenizer, content, maxTokens, overlapTokens, windowTokens,
            filepath, chunkStrategy, cancellationToken);

    // =========================================================================
    // Retrieval
    // =========================================================================
    public List<string> FindSimilarFiles(string query, int maxDistance = 3, int limit = 5) =>
        FuzzyMatcher.FindSimilarFiles(Db, query, maxDistance, limit);

    // =========================================================================
    // Indexing & Status
    // =========================================================================
    public IndexStatus GetStatus() => StatusOperations.GetStatus(Db);
    public IndexHealthInfo GetIndexHealth() => StatusOperations.GetIndexHealth(Db);
    public int GetHashesNeedingEmbedding() => StatusOperations.GetHashesNeedingEmbedding(Db);

    // =========================================================================
    // Maintenance
    // =========================================================================
    public int DeleteInactiveDocuments() => MaintenanceOperations.DeleteInactiveDocuments(Db);
    public int CleanupOrphanedContent() => MaintenanceOperations.CleanupOrphanedContent(Db);
    public void VacuumDatabase() => MaintenanceOperations.VacuumDatabase(Db);
    public int DeleteLLMCache() => MaintenanceOperations.DeleteLLMCache(Db);

    // =========================================================================
    // Cache
    // =========================================================================
    public string? GetCachedResult(string cacheKey) => CacheOperations.GetCachedResult(Db, cacheKey);
    public void SetCachedResult(string cacheKey, string result) => CacheOperations.SetCachedResult(Db, cacheKey, result);
    public void ClearCache() => CacheOperations.ClearCache(Db);

    // =========================================================================
    // LLM & Embeddings (Phase 3)
    // =========================================================================
    public ILlmService? LlmService { get; set; }

    public Task<List<SearchResult>> SearchVecAsync(string query, string model,
        int limit = 20, List<string>? collections = null, float[]? precomputedEmbedding = null, CancellationToken ct = default) =>
        VectorSearcher.SearchVecAsync(Db, query, model, LlmService, precomputedEmbedding, limit, collections, ct);

    public void InsertEmbedding(string hash, int seq, int pos, float[] embedding, string model, string embeddedAt) =>
        EmbeddingOperations.InsertEmbedding(Db, hash, seq, pos, embedding, model, embeddedAt);

    public List<PendingEmbeddingDoc> GetPendingEmbeddingDocs() =>
        EmbeddingOperations.GetPendingEmbeddingDocs(Db);

    public void ClearAllEmbeddings() => EmbeddingOperations.ClearAllEmbeddings(Db);

    public void EnsureVecTable(int dimensions) => VecExtension.EnsureVecTable(Db, dimensions);

    public async Task<EmbedResult> GenerateEmbeddingsAsync(ILlmService llmService, EmbedPipelineOptions? options = null) =>
        await EmbeddingPipeline.GenerateEmbeddingsAsync(Db, llmService, options, dims => EnsureVecTable(dims));

    // =========================================================================
    // Hybrid Search (Phase 4)
    // =========================================================================
    public async Task<List<HybridQueryResult>> HybridQueryAsync(ILlmService llmService,
        string query, HybridQueryOptions? options = null, CancellationToken ct = default) =>
        await HybridQueryService.HybridQueryAsync(this, llmService, query, options, ct);

    public async Task<List<ExpandedQuery>> ExpandQueryAsync(ILlmService llmService,
        string query, string? intent = null, CancellationToken ct = default) =>
        await QueryExpander.ExpandQueryAsync(Db, llmService, query, null, intent, ct);

    // =========================================================================
    // Context Resolution
    // =========================================================================
    public string? GetContextForFile(string filepath) =>
        ContextResolver.GetContextForFile(Db, filepath);

    // =========================================================================
    // Config Sync
    // =========================================================================
    public void SyncConfig(CollectionConfig config) => ConfigSync.SyncToDb(Db, config);

    public void Dispose() => Db.Dispose();
}
