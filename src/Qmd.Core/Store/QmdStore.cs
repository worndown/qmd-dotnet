using System.Diagnostics;
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
/// Central facade connecting all QMD subsystems. Implements <see cref="IQmdStore"/>.
/// </summary>
internal class QmdStore : IQmdStore, IDisposable
{
    private readonly ConfigManager _configManager;

    #region Constructors

    /// <summary>Create store with a file-based database and full config (production use).</summary>
    public QmdStore(string dbPath, ConfigManager configManager, ILlmService? llmService = null)
    {
        DbPath = dbPath;
        _configManager = configManager;
        LlmService = llmService;
        Db = new SqliteDatabase(DbPath);
        SchemaInitializer.Initialize(Db);
        VecExtension.TryLoad(Db);
        SyncConfig(configManager.LoadConfig());
    }

    /// <summary>Create store with a pre-opened database and full config.</summary>
    public QmdStore(IQmdDatabase db, ConfigManager configManager, ILlmService? llmService = null)
    {
        DbPath = ":memory:";
        _configManager = configManager;
        LlmService = llmService;
        Db = db;
        SchemaInitializer.Initialize(Db);
        VecExtension.TryLoad(Db);
        SyncConfig(configManager.LoadConfig());
    }

    /// <summary>Create store with a file-based database (for tests / low-level use).</summary>
    public QmdStore(string? dbPath = null)
    {
        DbPath = dbPath ?? QmdPaths.GetDefaultDbPath();
        _configManager = new ConfigManager(new InlineConfigSource(new CollectionConfig()));
        Db = new SqliteDatabase(DbPath);
        SchemaInitializer.Initialize(Db);
        VecExtension.TryLoad(Db);
    }

    /// <summary>Create store with a pre-opened database (for testing with :memory:).</summary>
    public QmdStore(IQmdDatabase db, string dbPath = ":memory:")
    {
        Db = db;
        DbPath = dbPath;
        _configManager = new ConfigManager(new InlineConfigSource(new CollectionConfig()));
        SchemaInitializer.Initialize(Db);
        VecExtension.TryLoad(Db);
    }

    #endregion

    public IQmdDatabase Db { get; }
    public string DbPath { get; }
    public ITokenizer Tokenizer { get; set; } = new CharBasedTokenizer();
    public ILlmService? LlmService { get; set; }

    private ILlmService GetLlmService() =>
        LlmService ?? throw new InvalidOperationException("LLM service not configured. Call EmbedAsync or configure LlamaSharpService.");

    #region Content

    public string HashContent(string content) => ContentHasher.HashContent(content);

    public void InsertContent(string hash, string content, string createdAt) =>
        ContentHasher.InsertContent(Db, hash, content, createdAt);

    public string ExtractTitle(string content, string filename) =>
        TitleExtractor.ExtractTitle(content, filename);

    #endregion

    #region Documents

    public void InsertDocument(string collection, string path, string title, string hash,
        string createdAt, string modifiedAt) =>
        DocumentOperations.InsertDocument(Db, collection, path, title, hash, createdAt, modifiedAt);

    public ActiveDocumentRow? FindActiveDocument(string collection, string path) =>
        DocumentOperations.FindActiveDocument(Db, collection, path);

    public void UpdateDocument(long id, string title, string hash, string modifiedAt) =>
        DocumentOperations.UpdateDocument(Db, id, title, hash, modifiedAt);

    public void DeactivateDocument(string collection, string path) =>
        DocumentOperations.DeactivateDocument(Db, collection, path);

    public List<string> GetActiveDocumentPaths(string collection) =>
        DocumentOperations.GetActiveDocumentPaths(Db, collection);

    #endregion

    #region Maintenance

    public int DeleteInactiveDocuments() => MaintenanceOperations.DeleteInactiveDocuments(Db);

    public Task<CleanupResult> CleanupAsync(CleanupOptions? options = null, CancellationToken ct = default)
    {
        options ??= new CleanupOptions();

        int cacheDeleted = 0, inactiveDeleted = 0, orphanedCollectionDocs = 0, orphanedContent = 0, orphanedVectors = 0;

        if (options.DeleteCache)
            cacheDeleted = MaintenanceOperations.DeleteLLMCache(Db);

        if (options.CleanOrphans)
            orphanedCollectionDocs = MaintenanceOperations.DeleteOrphanedCollectionDocuments(Db);

        if (options.DeleteInactive)
            inactiveDeleted = MaintenanceOperations.DeleteInactiveDocuments(Db);

        if (options.CleanOrphans)
        {
            orphanedContent = MaintenanceOperations.CleanupOrphanedContent(Db);
            orphanedVectors = MaintenanceOperations.CleanupOrphanedVectors(Db);
        }

        if (options.Vacuum)
            MaintenanceOperations.VacuumDatabase(Db);

        return Task.FromResult(new CleanupResult(
            CacheEntriesDeleted: cacheDeleted,
            InactiveDocsDeleted: inactiveDeleted,
            OrphanedCollectionDocsDeleted: orphanedCollectionDocs,
            OrphanedContentDeleted: orphanedContent,
            OrphanedVectorsDeleted: orphanedVectors,
            Vacuumed: options.Vacuum));
    }

    #endregion

    #region Cache

    public string? GetCachedResult(string cacheKey) => CacheOperations.GetCachedResult(Db, cacheKey);
    public void SetCachedResult(string cacheKey, string result) => CacheOperations.SetCachedResult(Db, cacheKey, result);
    public void ClearCache() => CacheOperations.ClearCache(Db);

    #endregion

    #region Search

    public async Task<List<HybridQueryResult>> SearchAsync(SearchOptions options, CancellationToken ct = default)
    {
        return await HybridQueryService.HybridQueryAsync(this, GetLlmService(), options.Query,
            new HybridQueryOptions
            {
                Collections = options.Collections,
                Limit = options.Limit,
                MinScore = options.MinScore,
                Intent = options.Intent,
                SkipRerank = options.SkipRerank,
                CandidateLimit = options.CandidateLimit,
                ChunkStrategy = options.ChunkStrategy,
                Explain = options.Explain,
                Diagnostics = options.Diagnostics,
            }, ct);
    }

    public async Task<List<HybridQueryResult>> SearchStructuredAsync(
        List<ExpandedQuery> searches, StructuredSearchOptions? options = null, CancellationToken ct = default)
    {
        return await StructuredSearchService.SearchAsync(this, GetLlmService(), searches, options, ct);
    }

    public Task<List<SearchResult>> SearchLexAsync(string query, LexSearchOptions? options = null)
    {
        options ??= new LexSearchOptions();
        return Task.FromResult(SearchFTS(query, options.Limit, options.Collections));
    }

    public async Task<List<SearchResult>> SearchVectorAsync(string query, VectorSearchOptions? options = null, CancellationToken ct = default)
    {
        options ??= new VectorSearchOptions();
        return await VectorSearchQueryService.SearchAsync(this, GetLlmService(), query,
            new VectorSearchQueryOptions
            {
                Limit = options.Limit,
                MinScore = options.MinScore,
                Collections = options.Collections,
                Intent = options.Intent,
            }, ct);
    }

    public async Task<List<ExpandedQuery>> ExpandQueryAsync(string query, ExpandQuerySdkOptions? options = null, CancellationToken ct = default)
    {
        return await QueryExpander.ExpandQueryAsync(Db, GetLlmService(), query, null, options?.Intent, ct);
    }

    public async Task<List<HybridQueryResult>> HybridQueryAsync(string query, HybridQueryOptions? options = null, CancellationToken ct = default)
    {
        return await HybridQueryService.HybridQueryAsync(this, GetLlmService(), query, options, ct);
    }

    public List<SearchResult> SearchFTS(string query, int limit = 20, List<string>? collections = null) =>
        FtsSearcher.SearchFTS(Db, query, limit, collections);

    public Task<List<SearchResult>> SearchVecAsync(string query, string model,
        int limit = 20, List<string>? collections = null, float[]? precomputedEmbedding = null, CancellationToken ct = default) =>
        VectorSearcher.SearchVecAsync(Db, query, model, LlmService, precomputedEmbedding, limit, collections, ct);

    #endregion

    #region Retrieval

    public Task<FindDocumentResult> GetAsync(string pathOrDocId, GetOptions? options = null)
    {
        var result = DocumentFinder.FindDocument(Db, pathOrDocId, options?.IncludeBody ?? false);
        return Task.FromResult(result);
    }

    public Task<string?> GetDocumentBodyAsync(string pathOrDocId, BodyOptions? options = null)
    {
        var findResult = DocumentFinder.FindDocument(Db, pathOrDocId);
        if (!findResult.IsFound) return Task.FromResult<string?>(null);
        var body = DocumentFinder.GetDocumentBody(Db, findResult.Document!.Filepath,
            options?.FromLine, options?.MaxLines);
        return Task.FromResult(body);
    }

    public Task<(List<MultiGetResult> Docs, List<string> Errors)> MultiGetAsync(string pattern, MultiGetOptions? options = null)
    {
        options ??= new MultiGetOptions();
        var (docs, errors) = MultiGetService.FindDocuments(Db, pattern, options.IncludeBody, options.MaxBytes);
        return Task.FromResult((docs, errors));
    }

    public Task<List<ListFileEntry>> ListFilesAsync(string collection, string? pathPrefix = null)
    {
        var results = new List<ListFileEntry>();
        List<ListFileRow> rows;
        if (pathPrefix != null)
        {
            rows = Db.Prepare(@"
                SELECT d.path, LENGTH(c.doc) AS size
                FROM documents d
                JOIN content c ON c.hash = d.hash
                WHERE d.collection = $1 AND d.path LIKE $2 AND d.active = 1
                ORDER BY d.path
            ").All<ListFileRow>(collection, pathPrefix + "%");
        }
        else
        {
            rows = Db.Prepare(@"
                SELECT d.path, LENGTH(c.doc) AS size
                FROM documents d
                JOIN content c ON c.hash = d.hash
                WHERE d.collection = $1 AND d.active = 1
                ORDER BY d.path
            ").All<ListFileRow>(collection);
        }

        foreach (var row in rows)
        {
            results.Add(new ListFileEntry
            {
                Path = row.Path,
                DisplayPath = row.Path,
                BodyLength = row.Size,
            });
        }
        return Task.FromResult(results);
    }

    public Task<string?> GetContextForFileAsync(string filepath)
    {
        return Task.FromResult(GetContextForFile(filepath));
    }

    public Task<List<string>> FindSimilarFilesAsync(string query, int maxDistance = 3, int limit = 5)
    {
        return Task.FromResult(FindSimilarFiles(query, maxDistance, limit));
    }

    public Task<List<string>> GetActiveDocumentPathsAsync(string collection)
    {
        return Task.FromResult(GetActiveDocumentPaths(collection));
    }

    public string? GetContextForFile(string filepath) =>
        ContextResolver.GetContextForFile(Db, filepath);

    public List<string> FindSimilarFiles(string query, int maxDistance = 3, int limit = 5) =>
        FuzzyMatcher.FindSimilarFiles(Db, query, maxDistance, limit);

    #endregion

    #region Collections

    public Task AddCollectionAsync(string name, string path, string pattern = "**/*.md", List<string>? ignore = null)
    {
        _configManager.AddCollection(name, path, pattern);
        if (ignore is { Count: > 0 })
        {
            var config = _configManager.LoadConfig();
            if (config.Collections.TryGetValue(name, out var coll))
            {
                coll.Ignore = ignore;
                _configManager.SaveConfig(config);
            }
        }
        SyncConfig(_configManager.LoadConfig());
        return Task.CompletedTask;
    }

    public Task<bool> RemoveCollectionAsync(string name)
    {
        var result = _configManager.RemoveCollection(name);
        if (result)
        {
            Db.Prepare("DELETE FROM documents WHERE collection = $1").Run(name);
            MaintenanceOperations.CleanupOrphanedContent(Db);
            MaintenanceOperations.CleanupOrphanedVectors(Db);
            SyncConfig(_configManager.LoadConfig());
        }
        return Task.FromResult(result);
    }

    public Task<bool> RenameCollectionAsync(string oldName, string newName)
    {
        var result = _configManager.RenameCollection(oldName, newName);
        if (result) SyncConfig(_configManager.LoadConfig());
        return Task.FromResult(result);
    }

    public Task<List<NamedCollection>> ListCollectionsAsync()
    {
        return Task.FromResult(_configManager.ListCollections());
    }

    public Task<List<string>> GetDefaultCollectionNamesAsync()
    {
        return Task.FromResult(_configManager.GetDefaultCollectionNames());
    }

    public Task<bool> UpdateCollectionSettingsAsync(string name, string? update = null, bool? includeByDefault = null, bool clearUpdate = false)
    {
        var result = _configManager.UpdateCollectionSettings(name, update, includeByDefault, clearUpdate);
        if (result) SyncConfig(_configManager.LoadConfig());
        return Task.FromResult(result);
    }

    public void SyncConfig(CollectionConfig config) => ConfigSync.SyncToDb(Db, config);

    #endregion

    #region Context

    public Task<bool> AddContextAsync(string collection, string pathPrefix, string text)
    {
        var result = _configManager.AddContext(collection, pathPrefix, text);
        if (result) SyncConfig(_configManager.LoadConfig());
        return Task.FromResult(result);
    }

    public Task<bool> RemoveContextAsync(string collection, string pathPrefix)
    {
        var result = _configManager.RemoveContext(collection, pathPrefix);
        if (result) SyncConfig(_configManager.LoadConfig());
        return Task.FromResult(result);
    }

    public Task SetGlobalContextAsync(string? context)
    {
        _configManager.SetGlobalContext(context);
        SyncConfig(_configManager.LoadConfig());
        return Task.CompletedTask;
    }

    public Task<string?> GetGlobalContextAsync()
    {
        return Task.FromResult(_configManager.GetGlobalContext());
    }

    public Task<List<(string Collection, string Path, string Context)>> ListContextsAsync()
    {
        return Task.FromResult(_configManager.ListAllContexts());
    }

    #endregion

    #region Indexing

    public async Task<ReindexResult> UpdateAsync(UpdateOptions? options = null, CancellationToken ct = default)
    {
        CacheOperations.ClearCache(Db);

        var collections = options?.Collections != null
            ? _configManager.ListCollections().Where(c => options.Collections.Contains(c.Name)).ToList()
            : _configManager.ListCollections();

        int totalIndexed = 0, totalUpdated = 0, totalUnchanged = 0, totalRemoved = 0, totalOrphaned = 0;

        foreach (var coll in collections)
        {
            ct.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(coll.Update))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                    Arguments = OperatingSystem.IsWindows() ? $"/c {coll.Update}" : $"-c \"{coll.Update}\"",
                    WorkingDirectory = coll.Path,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                    await proc.WaitForExitAsync(ct);
            }

            var result = await CollectionReindexer.ReindexCollectionAsync(
                this, coll.Path, coll.Pattern, coll.Name,
                new ReindexOptions
                {
                    IgnorePatterns = coll.Ignore,
                    OnProgress = options?.OnProgress,
                });
            totalIndexed += result.Indexed;
            totalUpdated += result.Updated;
            totalUnchanged += result.Unchanged;
            totalRemoved += result.Removed;
            totalOrphaned += result.OrphanedCleaned;
        }

        var health = await GetIndexHealthAsync();
        return new ReindexResult(totalIndexed, totalUpdated, totalUnchanged, totalRemoved, totalOrphaned,
            Collections: collections.Count(), NeedsEmbedding: health.NeedsEmbedding);
    }

    public async Task<EmbedResult> EmbedAsync(EmbedPipelineOptions? options = null, CancellationToken ct = default)
    {
        return await EmbeddingPipeline.GenerateEmbeddingsAsync(Db, GetLlmService(), options,
            dims => VecExtension.EnsureVecTable(Db, dims));
    }

    #endregion

    #region Diagnostics

    public async Task<EmbeddingProfile> ProfileEmbeddingsAsync(EmbeddingProfileOptions? options = null, CancellationToken ct = default)
    {
        return await EmbeddingProfiler.ProfileAsync(Db, GetLlmService(), options, ct);
    }

    #endregion

    #region Health

    public Task<IndexStatus> GetStatusAsync()
    {
        return Task.FromResult(GetStatus());
    }

    public Task<IndexHealthInfo> GetIndexHealthAsync()
    {
        return Task.FromResult(GetIndexHealth());
    }

    public IndexStatus GetStatus() => StatusOperations.GetStatus(Db);
    public IndexHealthInfo GetIndexHealth() => StatusOperations.GetIndexHealth(Db);

    #endregion

    #region Lifecycle

    public async ValueTask DisposeAsync()
    {
        if (LlmService != null)
            await LlmService.DisposeAsync();
        Db.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        Db.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}
