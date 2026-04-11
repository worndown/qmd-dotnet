using System.Diagnostics;
using Qmd.Core.Configuration;
using Qmd.Core.Indexing;
using Qmd.Core.Llm;
using Qmd.Core.Models;
using Qmd.Core.Retrieval;
using Qmd.Core.Search;

namespace Qmd.Core.Store;

/// <summary>
/// Implementation wrapping Qmd.Core.Store.QmdStore.
/// </summary>
internal class QmdStoreImpl : IQmdStore
{
    private readonly QmdStore _store;
    private readonly ConfigManager _configManager;
    private ILlmService? _llmService;

    public QmdStoreImpl(QmdStore store, ConfigManager configManager, ILlmService? llmService = null)
    {
        _store = store;
        _configManager = configManager;
        _llmService = llmService;
        _store.LlmService = llmService;
    }

    private ILlmService GetLlmService() =>
        _llmService ?? throw new InvalidOperationException("LLM service not configured. Call EmbedAsync or configure LlamaSharpService.");

    #region Search

    public async Task<List<HybridQueryResult>> SearchAsync(SearchOptions options, CancellationToken ct = default)
    {
        return await HybridQueryService.HybridQueryAsync(_store, GetLlmService(), options.Query,
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
        return await StructuredSearchService.SearchAsync(_store, GetLlmService(), searches, options, ct);
    }

    public Task<List<SearchResult>> SearchLexAsync(string query, LexSearchOptions? options = null)
    {
        options ??= new LexSearchOptions();
        return Task.FromResult(_store.SearchFTS(query, options.Limit, options.Collections));
    }

    public async Task<List<SearchResult>> SearchVectorAsync(string query, VectorSearchOptions? options = null, CancellationToken ct = default)
    {
        options ??= new VectorSearchOptions();
        return await VectorSearchQueryService.SearchAsync(_store, GetLlmService(), query,
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
        return await _store.ExpandQueryAsync(GetLlmService(), query, options?.Intent, ct);
    }

    public async Task<List<HybridQueryResult>> HybridQueryAsync(string query, HybridQueryOptions? options = null, CancellationToken ct = default)
    {
        return await HybridQueryService.HybridQueryAsync(_store, GetLlmService(), query, options, ct);
    }

    #endregion

    #region Retrieval

    public Task<FindDocumentResult> GetAsync(string pathOrDocId, GetOptions? options = null)
    {
        var result = DocumentFinder.FindDocument(_store.Db, pathOrDocId, options?.IncludeBody ?? false);
        return Task.FromResult(result);
    }

    public Task<string?> GetDocumentBodyAsync(string pathOrDocId, BodyOptions? options = null)
    {
        var findResult = DocumentFinder.FindDocument(_store.Db, pathOrDocId);
        if (!findResult.IsFound) return Task.FromResult<string?>(null);
        var body = DocumentFinder.GetDocumentBody(_store.Db, findResult.Document!.Filepath,
            options?.FromLine, options?.MaxLines);
        return Task.FromResult(body);
    }

    public Task<(List<MultiGetResult> Docs, List<string> Errors)> MultiGetAsync(string pattern, MultiGetOptions? options = null)
    {
        options ??= new MultiGetOptions();
        var (docs, errors) = MultiGetService.FindDocuments(
            _store.Db, pattern, options.IncludeBody, options.MaxBytes);
        return Task.FromResult((docs, errors));
    }

    public Task<string?> GetContextForFileAsync(string filepath)
    {
        return Task.FromResult(_store.GetContextForFile(filepath));
    }

    public Task<List<string>> FindSimilarFilesAsync(string query, int maxDistance = 3, int limit = 5)
    {
        return Task.FromResult(_store.FindSimilarFiles(query, maxDistance, limit));
    }

    public Task<List<string>> GetActiveDocumentPathsAsync(string collection)
    {
        return Task.FromResult(_store.GetActiveDocumentPaths(collection));
    }

    public Task<List<ListFileEntry>> ListFilesAsync(string collection, string? pathPrefix = null)
    {
        var results = new List<ListFileEntry>();
        IEnumerable<dynamic> rows;
        if (pathPrefix != null)
        {
            rows = _store.Db.Prepare(@"
                SELECT d.path, LENGTH(c.doc) AS size
                FROM documents d
                JOIN content c ON c.hash = d.hash
                WHERE d.collection = $1 AND d.path LIKE $2 AND d.active = 1
                ORDER BY d.path
            ").AllDynamic(collection, pathPrefix + "%");
        }
        else
        {
            rows = _store.Db.Prepare(@"
                SELECT d.path, LENGTH(c.doc) AS size
                FROM documents d
                JOIN content c ON c.hash = d.hash
                WHERE d.collection = $1 AND d.active = 1
                ORDER BY d.path
            ").AllDynamic(collection);
        }

        foreach (var row in rows)
        {
            results.Add(new ListFileEntry
            {
                Path = row["path"]!.ToString()!,
                DisplayPath = row["path"]!.ToString()!,
                BodyLength = Convert.ToInt32(row["size"]),
            });
        }
        return Task.FromResult(results);
    }

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
        _store.SyncConfig(_configManager.LoadConfig());
        return Task.CompletedTask;
    }

    public Task<bool> RemoveCollectionAsync(string name)
    {
        var result = _configManager.RemoveCollection(name);
        if (result)
        {
            // Delete documents belonging to this collection
            _store.Db.Prepare("DELETE FROM documents WHERE collection = $1").Run(name);
            // Clean up orphaned content and vectors
            MaintenanceOperations.CleanupOrphanedContent(_store.Db);
            MaintenanceOperations.CleanupOrphanedVectors(_store.Db);
            _store.SyncConfig(_configManager.LoadConfig());
        }
        return Task.FromResult(result);
    }

    public Task<bool> RenameCollectionAsync(string oldName, string newName)
    {
        var result = _configManager.RenameCollection(oldName, newName);
        if (result) _store.SyncConfig(_configManager.LoadConfig());
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
        if (result) _store.SyncConfig(_configManager.LoadConfig());
        return Task.FromResult(result);
    }

    #endregion

    #region Context

    public Task<bool> AddContextAsync(string collection, string pathPrefix, string text)
    {
        var result = _configManager.AddContext(collection, pathPrefix, text);
        if (result) _store.SyncConfig(_configManager.LoadConfig());
        return Task.FromResult(result);
    }

    public Task<bool> RemoveContextAsync(string collection, string pathPrefix)
    {
        var result = _configManager.RemoveContext(collection, pathPrefix);
        if (result) _store.SyncConfig(_configManager.LoadConfig());
        return Task.FromResult(result);
    }

    public Task SetGlobalContextAsync(string? context)
    {
        _configManager.SetGlobalContext(context);
        _store.SyncConfig(_configManager.LoadConfig());
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
        // Clear LLM cache before reindexing so stale query expansions don't persist
        CacheOperations.ClearCache(_store.Db);

        var collections = options?.Collections != null
            ? _configManager.ListCollections().Where(c => options.Collections.Contains(c.Name)).ToList()
            : _configManager.ListCollections();

        int totalIndexed = 0, totalUpdated = 0, totalUnchanged = 0, totalRemoved = 0, totalOrphaned = 0;

        foreach (var coll in collections)
        {
            ct.ThrowIfCancellationRequested();

            // Execute custom update command if configured
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
                _store, coll.Path, coll.Pattern, coll.Name,
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
        return await _store.GenerateEmbeddingsAsync(GetLlmService(), options);
    }

    #endregion

    #region Diagnostics

    public async Task<EmbeddingProfile> ProfileEmbeddingsAsync(EmbeddingProfileOptions? options = null, CancellationToken ct = default)
    {
        return await EmbeddingProfiler.ProfileAsync(_store.Db, GetLlmService(), options, ct);
    }

    #endregion

    #region Health

    public Task<IndexStatus> GetStatusAsync()
    {
        return Task.FromResult(_store.GetStatus());
    }

    public Task<IndexHealthInfo> GetIndexHealthAsync()
    {
        return Task.FromResult(_store.GetIndexHealth());
    }

    #endregion

    #region Lifecycle

    public async ValueTask DisposeAsync()
    {
        if (_llmService != null)
            await _llmService.DisposeAsync();
        _store.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}
