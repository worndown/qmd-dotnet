using System.Diagnostics;
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
    private readonly ConfigManager configManager;

    // Repositories (initialized by ApplyServices, called in every constructor)
    internal IDocumentRepository DocumentRepo { get; private set; } = null!;
    internal IMaintenanceRepository MaintenanceRepo { get; private set; } = null!;
    internal IStatusRepository StatusRepo { get; private set; } = null!;
    internal IEmbeddingRepository EmbeddingRepo { get; private set; } = null!;
    internal ICacheRepository CacheRepo { get; private set; } = null!;
    internal IConfigSyncService ConfigSyncService { get; private set; } = null!;

    // Search services (initialized by ApplyServices, called in every constructor)
    internal IFtsSearchService FtsSearch { get; private set; } = null!;
    internal IVectorSearchService VectorSearch { get; private set; } = null!;

    private static QmdStoreServices CreateDefaultServices(IQmdDatabase db, ILlmService? llmService = null) => new(
        DocumentRepo: new DocumentRepository(db),
        MaintenanceRepo: new MaintenanceRepository(db),
        StatusRepo: new StatusRepository(db),
        EmbeddingRepo: new EmbeddingRepository(db),
        CacheRepo: new CacheRepository(db),
        ConfigSync: new ConfigSyncService(db),
        FtsSearch: new FtsSearchService(db),
        VectorSearch: new VectorSearchService(db, llmService)
    );

    private void ApplyServices(QmdStoreServices services)
    {
        this.DocumentRepo = services.DocumentRepo;
        this.MaintenanceRepo = services.MaintenanceRepo;
        this.StatusRepo = services.StatusRepo;
        this.EmbeddingRepo = services.EmbeddingRepo;
        this.CacheRepo = services.CacheRepo;
        this.ConfigSyncService = services.ConfigSync;
        this.FtsSearch = services.FtsSearch;
        this.VectorSearch = services.VectorSearch;
    }

    #region Constructors

    /// <summary>Create store with a file-based database and full config (production use).</summary>
    public QmdStore(string dbPath, ConfigManager configManager, ILlmService? llmService = null)
    {
        this.DbPath = dbPath;
        this.configManager = configManager;
        this.LlmService = llmService;
        this.Db = new SqliteDatabase(this.DbPath);
        this.ApplyServices(CreateDefaultServices(this.Db, llmService));
        SchemaInitializer.Initialize(this.Db);
        VecExtension.TryLoad(this.Db);
        this.SearchConfig = SearchConfigRepository.Load(this.Db);
        this.SyncConfig(configManager.LoadConfig());
    }

    /// <summary>Create store with a pre-opened database and full config.</summary>
    public QmdStore(IQmdDatabase db, ConfigManager configManager, ILlmService? llmService = null)
    {
        this.DbPath = ":memory:";
        this.configManager = configManager;
        this.LlmService = llmService;
        this.Db = db;
        this.ApplyServices(CreateDefaultServices(this.Db, llmService));
        SchemaInitializer.Initialize(this.Db);
        VecExtension.TryLoad(this.Db);
        this.SearchConfig = SearchConfigRepository.Load(this.Db);
        this.SyncConfig(configManager.LoadConfig());
    }

    /// <summary>Create store with a file-based database (for tests / low-level use).</summary>
    public QmdStore(string? dbPath = null)
    {
        this.DbPath = dbPath ?? QmdPaths.GetDefaultDbPath();
        this.configManager = new ConfigManager(new InlineConfigSource(new CollectionConfig()));
        this.Db = new SqliteDatabase(this.DbPath);
        this.ApplyServices(CreateDefaultServices(this.Db));
        SchemaInitializer.Initialize(this.Db);
        VecExtension.TryLoad(this.Db);
    }

    /// <summary>Create store with a pre-opened database (for testing with :memory:).</summary>
    public QmdStore(IQmdDatabase db, string dbPath = ":memory:")
    {
        this.Db = db;
        this.DbPath = dbPath;
        this.configManager = new ConfigManager(new InlineConfigSource(new CollectionConfig()));
        this.ApplyServices(CreateDefaultServices(this.Db));
        SchemaInitializer.Initialize(this.Db);
        VecExtension.TryLoad(this.Db);
    }

    #endregion

    public IQmdDatabase Db { get; }
    public string DbPath { get; }
    public ILlmService? LlmService { get; set; }
    public SearchConfig SearchConfig { get; set; } = new();

    private ILlmService GetLlmService() => this.LlmService ?? throw new QmdModelException("LLM service not configured. Call EmbedAsync or configure LlamaSharpService.");

    private HybridQueryService CreateHybridQueryService()
    {
        var llm = this.GetLlmService();
        return new HybridQueryService(this.FtsSearch, this.VectorSearch,
            new QueryExpanderService(this.Db, llm),
            new RerankerService(this.Db, llm), this.Db, llm, this.SearchConfig);
    }

    private StructuredSearchService CreateStructuredSearchService()
    {
        var llm = this.GetLlmService();
        return new StructuredSearchService(this.FtsSearch, this.VectorSearch,
            new RerankerService(this.Db, llm), this.Db, llm);
    }

    #region Content

    public string HashContent(string content) => ContentHasher.HashContent(content);

    public void InsertContent(string hash, string content, string createdAt) => this.DocumentRepo.InsertContent(hash, content, createdAt);

    public string ExtractTitle(string content, string filename) =>
        TitleExtractor.ExtractTitle(content, filename);

    #endregion

    #region Documents

    public void InsertDocument(string collection, string path, string title, string hash,
        string createdAt, string modifiedAt) =>
        this.DocumentRepo.InsertDocument(collection, path, title, hash, createdAt, modifiedAt);

    public ActiveDocumentRow? FindActiveDocument(string collection, string path) => this.DocumentRepo.FindActiveDocument(collection, path);

    public void UpdateDocument(long id, string title, string hash, string modifiedAt) => this.DocumentRepo.UpdateDocument(id, title, hash, modifiedAt);

    public void DeactivateDocument(string collection, string path) => this.DocumentRepo.DeactivateDocument(collection, path);

    public List<string> GetActiveDocumentPaths(string collection) => this.DocumentRepo.GetActiveDocumentPaths(collection);

    #endregion

    #region Maintenance

    public int DeleteInactiveDocuments() => this.MaintenanceRepo.DeleteInactiveDocuments();

    public Task<CleanupResult> CleanupAsync(CleanupOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        options ??= new CleanupOptions();

        int cacheDeleted = 0, inactiveDeleted = 0, orphanedCollectionDocs = 0, orphanedContent = 0, orphanedVectors = 0;

        if (options.DeleteCache)
            cacheDeleted = this.MaintenanceRepo.DeleteLLMCache();

        if (options.CleanOrphans)
            orphanedCollectionDocs = this.MaintenanceRepo.DeleteOrphanedCollectionDocuments();

        if (options.DeleteInactive)
            inactiveDeleted = this.MaintenanceRepo.DeleteInactiveDocuments();

        if (options.CleanOrphans)
        {
            orphanedContent = this.MaintenanceRepo.CleanupOrphanedContent();
            orphanedVectors = this.MaintenanceRepo.CleanupOrphanedVectors();
        }

        if (options.Vacuum) this.MaintenanceRepo.VacuumDatabase();

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

    public string? GetCachedResult(string cacheKey) => this.CacheRepo.GetCachedResult(cacheKey);
    public void SetCachedResult(string cacheKey, string result) => this.CacheRepo.SetCachedResult(cacheKey, result);
    public void ClearCache() => this.CacheRepo.ClearCache();

    #endregion

    #region Search

    public async Task<List<HybridQueryResult>> SearchAsync(SearchOptions options, CancellationToken ct = default)
    {
        return await this.CreateHybridQueryService().HybridQueryAsync(options.Query,
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
        return await this.CreateStructuredSearchService().SearchAsync(searches, options, ct);
    }

    public Task<List<SearchResult>> SearchLexAsync(string query, LexSearchOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        options ??= new LexSearchOptions();
        return Task.FromResult(this.FtsSearch.Search(query, options.Limit, options.Collections));
    }

    public async Task<List<SearchResult>> SearchVectorAsync(string query, VectorSearchOptions? options = null, CancellationToken ct = default)
    {
        options ??= new VectorSearchOptions();
        var llm = this.GetLlmService();
        var vecQueryService = new VectorSearchQueryService(this.VectorSearch,
            new QueryExpanderService(this.Db, llm), this.Db, llm);
        return await vecQueryService.SearchAsync(query,
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
        return await new QueryExpanderService(this.Db, this.GetLlmService())
            .ExpandQueryAsync(query, null, options?.Intent, ct);
    }

    public async Task<List<HybridQueryResult>> HybridQueryAsync(string query, HybridQueryOptions? options = null, CancellationToken ct = default)
    {
        return await this.CreateHybridQueryService().HybridQueryAsync(query, options, ct);
    }

    public List<SearchResult> SearchFTS(string query, int limit = 20, List<string>? collections = null) => this.FtsSearch.Search(query, limit, collections);

    public Task<List<SearchResult>> SearchVecAsync(string query, string model,
        int limit = 20, List<string>? collections = null, float[]? precomputedEmbedding = null, CancellationToken ct = default) =>
        this.VectorSearch.SearchAsync(query, model, limit, collections, precomputedEmbedding, ct);

    #endregion

    #region Retrieval

    public Task<FindDocumentResult> GetAsync(string pathOrDocId, GetOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var docFinder = new DocumentFinderService(this.Db, new FuzzyMatcherService(this.Db), new ContextResolverService(this.Db));
        var result = docFinder.FindDocument(pathOrDocId, options?.IncludeBody ?? false);
        return Task.FromResult(result);
    }

    public Task<string?> GetDocumentBodyAsync(string pathOrDocId, BodyOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var docFinder = new DocumentFinderService(this.Db, new FuzzyMatcherService(this.Db), new ContextResolverService(this.Db));
        var findResult = docFinder.FindDocument(pathOrDocId);
        if (!findResult.IsFound) return Task.FromResult<string?>(null);
        var body = docFinder.GetDocumentBody(findResult.Document!.Filepath,
            options?.FromLine, options?.MaxLines);
        return Task.FromResult(body);
    }

    public Task<(List<MultiGetResult> Docs, List<string> Errors)> MultiGetAsync(string pattern, MultiGetOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        options ??= new MultiGetOptions();
        var contextResolver = new ContextResolverService(this.Db);
        var fuzzyMatcher = new FuzzyMatcherService(this.Db);
        var docFinder = new DocumentFinderService(this.Db, fuzzyMatcher, contextResolver);
        var multiGet = new MultiGetService(this.Db, docFinder, contextResolver);
        var (docs, errors) = multiGet.FindDocuments(pattern, options.IncludeBody, options.MaxBytes);
        return Task.FromResult((docs, errors));
    }

    public Task<List<ListFileEntry>> ListFilesAsync(string collection, string? pathPrefix = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var results = new List<ListFileEntry>();
        List<ListFileRow> rows;
        if (pathPrefix != null)
        {
            rows = this.Db.Prepare(@"
                SELECT d.path, LENGTH(c.doc) AS size
                FROM documents d
                JOIN content c ON c.hash = d.hash
                WHERE d.collection = $1 AND d.path LIKE $2 AND d.active = 1
                ORDER BY d.path
            ").All<ListFileRow>(collection, pathPrefix + "%");
        }
        else
        {
            rows = this.Db.Prepare(@"
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
        return Task.FromResult(this.GetContextForFile(filepath));
    }

    public Task<List<string>> FindSimilarFilesAsync(string query, int maxDistance = 3, int limit = 5, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(this.FindSimilarFiles(query, maxDistance, limit));
    }

    public Task<List<string>> GetActiveDocumentPathsAsync(string collection, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(this.GetActiveDocumentPaths(collection));
    }

    public string? GetContextForFile(string filepath) =>
        ContextResolver.GetContextForFile(this.Db, filepath);

    public List<string> FindSimilarFiles(string query, int maxDistance = 3, int limit = 5) =>
        FuzzyMatcher.FindSimilarFiles(this.Db, query, maxDistance, limit);

    #endregion

    #region Collections

    public Task AddCollectionAsync(string name, string path, string pattern = "**/*.md", List<string>? ignore = null)
    {
        this.configManager.AddCollection(name, path, pattern);
        if (ignore is { Count: > 0 })
        {
            var config = this.configManager.LoadConfig();
            if (config.Collections.TryGetValue(name, out var coll))
            {
                coll.Ignore = ignore;
                this.configManager.SaveConfig(config);
            }
        }

        this.SyncConfig(this.configManager.LoadConfig());
        return Task.CompletedTask;
    }

    public Task<bool> RemoveCollectionAsync(string name)
    {
        var result = this.configManager.RemoveCollection(name);
        if (result)
        {
            this.Db.Prepare("DELETE FROM documents WHERE collection = $1").Run(name);
            this.MaintenanceRepo.CleanupOrphanedContent();
            this.MaintenanceRepo.CleanupOrphanedVectors();
            this.SyncConfig(this.configManager.LoadConfig());
        }
        return Task.FromResult(result);
    }

    public Task<bool> RenameCollectionAsync(string oldName, string newName)
    {
        var result = this.configManager.RenameCollection(oldName, newName);
        if (result) this.SyncConfig(this.configManager.LoadConfig());
        return Task.FromResult(result);
    }

    public Task<List<NamedCollection>> ListCollectionsAsync()
    {
        return Task.FromResult(this.configManager.ListCollections());
    }

    public Task<List<string>> GetDefaultCollectionNamesAsync()
    {
        return Task.FromResult(this.configManager.GetDefaultCollectionNames());
    }

    public Task<bool> UpdateCollectionSettingsAsync(string name, string? update = null, bool? includeByDefault = null, bool clearUpdate = false)
    {
        var result = this.configManager.UpdateCollectionSettings(name, update, includeByDefault, clearUpdate);
        if (result) this.SyncConfig(this.configManager.LoadConfig());
        return Task.FromResult(result);
    }

    public void SyncConfig(CollectionConfig config) => this.ConfigSyncService.SyncToDb(config);

    #endregion

    #region Context

    public Task<bool> AddContextAsync(string collection, string pathPrefix, string text)
    {
        var result = this.configManager.AddContext(collection, pathPrefix, text);
        if (result) this.SyncConfig(this.configManager.LoadConfig());
        return Task.FromResult(result);
    }

    public Task<bool> RemoveContextAsync(string collection, string pathPrefix)
    {
        var result = this.configManager.RemoveContext(collection, pathPrefix);
        if (result) this.SyncConfig(this.configManager.LoadConfig());
        return Task.FromResult(result);
    }

    public Task SetGlobalContextAsync(string? context)
    {
        this.configManager.SetGlobalContext(context);
        this.SyncConfig(this.configManager.LoadConfig());
        return Task.CompletedTask;
    }

    public Task<string?> GetGlobalContextAsync()
    {
        return Task.FromResult(this.configManager.GetGlobalContext());
    }

    public Task<List<(string Collection, string Path, string Context)>> ListContextsAsync()
    {
        return Task.FromResult(this.configManager.ListAllContexts());
    }

    #endregion

    #region Indexing

    public async Task<ReindexResult> UpdateAsync(UpdateOptions? options = null, CancellationToken ct = default)
    {
        this.CacheRepo.ClearCache();

        var collections = options?.Collections != null
            ? this.configManager.ListCollections().Where(c => options.Collections.Contains(c.Name)).ToList()
            : this.configManager.ListCollections();

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

            var result = await new CollectionReindexerService(this.DocumentRepo, this.MaintenanceRepo)
                .ReindexCollectionAsync(coll.Path, coll.Pattern, coll.Name,
                new ReindexOptions
                {
                    IgnorePatterns = coll.Ignore,
                    Progress = options?.Progress,
                }, ct);
            totalIndexed += result.Indexed;
            totalUpdated += result.Updated;
            totalUnchanged += result.Unchanged;
            totalRemoved += result.Removed;
            totalOrphaned += result.OrphanedCleaned;
        }

        var health = await this.GetIndexHealthAsync();
        return new ReindexResult(totalIndexed, totalUpdated, totalUnchanged, totalRemoved, totalOrphaned,
            Collections: collections.Count(), NeedsEmbedding: health.NeedsEmbedding);
    }

    public async Task<EmbedResult> EmbedAsync(EmbedPipelineOptions? options = null, CancellationToken ct = default)
    {
        options ??= new EmbedPipelineOptions();
        if (ct != default && options.CancellationToken == default)
            options = new EmbedPipelineOptions
            {
                Force = options.Force, Model = options.Model,
                MaxDocsPerBatch = options.MaxDocsPerBatch, MaxBatchBytes = options.MaxBatchBytes,
                ChunkStrategy = options.ChunkStrategy, Progress = options.Progress,
                CancellationToken = ct,
            };
        return await new EmbeddingPipelineService(this.Db, this.GetLlmService(), this.EmbeddingRepo)
            .GenerateEmbeddingsAsync(options, dims => VecExtension.EnsureVecTable(this.Db, dims));
    }

    #endregion

    #region Diagnostics

    public async Task<EmbeddingProfile> ProfileEmbeddingsAsync(EmbeddingProfileOptions? options = null, CancellationToken ct = default)
    {
        return await EmbeddingProfiler.ProfileAsync(this.Db, this.GetLlmService(), options, ct);
    }

    public Task SaveSearchConfigAsync(SearchConfig config)
    {
        SearchConfigRepository.Save(this.Db, config);
        this.SearchConfig = config;
        return Task.CompletedTask;
    }

    public Task ResetSearchConfigAsync()
    {
        SearchConfigRepository.Delete(this.Db);
        this.SearchConfig = new SearchConfig();
        return Task.CompletedTask;
    }

    #endregion

    #region Health

    public Task<IndexStatus> GetStatusAsync()
    {
        return Task.FromResult(this.GetStatus());
    }

    public Task<IndexHealthInfo> GetIndexHealthAsync()
    {
        return Task.FromResult(this.GetIndexHealth());
    }

    public IndexStatus GetStatus() => this.StatusRepo.GetStatus();
    public IndexHealthInfo GetIndexHealth() => this.StatusRepo.GetIndexHealth();

    #endregion

    #region Lifecycle

    public async ValueTask DisposeAsync()
    {
        if (this.LlmService != null)
            await this.LlmService.DisposeAsync();
        this.Db.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        this.Db.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}
