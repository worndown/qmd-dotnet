# Phase 5: Convert Stateful Static Services to Instance Classes with DI

## Goal

Convert the ~20 static service classes that depend on `IQmdDatabase` or `QmdStore` into instance classes with interfaces, enabling proper dependency injection, testability, and clear ownership of dependencies. This is the most impactful structural change in the refactoring.

## Why This Matters

The current architecture has a "god object" pattern: `QmdStore` is a 500-line facade that delegates to ~20 static classes, passing `this` (or `this.Db`) as the first argument to every call. This is a direct port of TypeScript's module pattern where stateless functions receive their dependencies as parameters.

In idiomatic C#, services receive dependencies through constructor injection. This:
- Makes dependencies explicit and visible
- Enables unit testing with mocks
- Eliminates the `QmdStore`-as-parameter coupling
- Allows services to be composed and replaced independently

## Architecture After Refactoring

```
QmdStoreFactory
  |
  +-- creates --> QmdStore (facade, implements IQmdStore)
                    |-- IFtsSearchService
                    |-- IVectorSearchService  
                    |-- IQueryExpanderService
                    |-- IRerankerService
                    |-- IHybridQueryService (depends on above 4)
                    |-- IStructuredSearchService (depends on above 4)
                    |-- IDocumentFinderService
                    |-- IMultiGetService
                    |-- IContextResolverService
                    |-- ICollectionReindexerService
                    |-- IEmbeddingPipelineService
                    |-- IDocumentRepository
                    |-- IMaintenanceRepository
                    |-- IStatusRepository
                    |-- IEmbeddingRepository
                    |-- ICacheRepository
                    |-- IConfigSyncService
```

## Sub-Phases

This phase is large and should be executed in sub-phases. Each sub-phase is independently committable and testable.

---

### Phase 5a: Data Access Repositories

Convert static classes that purely wrap SQL queries (no complex logic, just CRUD).

#### New Interfaces and Implementations

**`src/Qmd.Core/Documents/IDocumentRepository.cs`**:
```csharp
internal interface IDocumentRepository
{
    void InsertDocument(string collection, string path, string title, string hash,
        string createdAt, string modifiedAt);
    ActiveDocumentRow? FindActiveDocument(string collection, string path);
    void UpdateDocument(long id, string title, string hash, string modifiedAt);
    void UpdateDocumentTitle(long id, string title, string modifiedAt);
    void DeactivateDocument(string collection, string path);
    List<string> GetActiveDocumentPaths(string collection);
    void InsertContent(string hash, string content, string createdAt);
}
```

**Convert `DocumentOperations` -> `DocumentRepository : IDocumentRepository`**:
- Remove `static` keyword from class
- Remove `IQmdDatabase db` parameter from all methods
- Add constructor: `public DocumentRepository(IQmdDatabase db)`
- Store `db` as `private readonly IQmdDatabase _db`
- Move `ContentHasher.InsertContent` here as `InsertContent`

**`src/Qmd.Core/Indexing/IMaintenanceRepository.cs`**:
```csharp
internal interface IMaintenanceRepository
{
    int DeleteInactiveDocuments();
    int DeleteLLMCache();
    int DeleteOrphanedCollectionDocuments();
    int CleanupOrphanedContent();
    int CleanupOrphanedVectors();
    void VacuumDatabase();
}
```

**Convert `MaintenanceOperations` -> `MaintenanceRepository : IMaintenanceRepository`**

**`src/Qmd.Core/Indexing/IStatusRepository.cs`**:
```csharp
internal interface IStatusRepository
{
    IndexStatus GetStatus();
    IndexHealthInfo GetIndexHealth();
}
```

**Convert `StatusOperations` -> `StatusRepository : IStatusRepository`**

**`src/Qmd.Core/Embedding/IEmbeddingRepository.cs`**:
```csharp
internal interface IEmbeddingRepository
{
    List<PendingEmbeddingDoc> GetPendingEmbeddingDocs();
    List<EmbeddingDoc> GetEmbeddingDocsForBatch(List<PendingEmbeddingDoc> batch);
    void InsertEmbedding(string hash, int seq, int pos, float[] embedding, string model, string createdAt);
    void ClearAllEmbeddings();
    void ResetVecTableCache();
}
```

**Convert `EmbeddingOperations` -> `EmbeddingRepository : IEmbeddingRepository`**

**`src/Qmd.Core/Indexing/ICacheRepository.cs`**:
```csharp
internal interface ICacheRepository
{
    string? GetCachedResult(string cacheKey);
    void SetCachedResult(string cacheKey, string result);
    void ClearCache();
}
```

**Convert `CacheOperations` -> `CacheRepository : ICacheRepository`**

**`src/Qmd.Core/Configuration/IConfigSyncService.cs`**:
```csharp
internal interface IConfigSyncService
{
    void SyncToDb(CollectionConfig config);
}
```

**Convert `ConfigSync` -> `ConfigSyncService : IConfigSyncService`**

#### Update QmdStore

Replace static calls with injected repositories:

```csharp
internal class QmdStore : IQmdStore, IDisposable
{
    private readonly IDocumentRepository _documentRepo;
    private readonly IMaintenanceRepository _maintenanceRepo;
    private readonly IStatusRepository _statusRepo;
    private readonly ICacheRepository _cacheRepo;
    private readonly IConfigSyncService _configSync;
    // ...

    public QmdStore(IQmdDatabase db, ConfigManager configManager,
        IDocumentRepository documentRepo,
        IMaintenanceRepository maintenanceRepo,
        IStatusRepository statusRepo,
        ICacheRepository cacheRepo,
        IConfigSyncService configSync,
        ILlmService? llmService = null)
    {
        // ...
    }
}
```

#### Update QmdStoreFactory

Compose the object graph:

```csharp
public static Task<IQmdStore> CreateAsync(StoreOptions options)
{
    // ... existing config logic ...
    var db = new SqliteDatabase(options.DbPath);
    var documentRepo = new DocumentRepository(db);
    var maintenanceRepo = new MaintenanceRepository(db);
    var statusRepo = new StatusRepository(db);
    var cacheRepo = new CacheRepository(db);
    var configSync = new ConfigSyncService(db);

    var store = new QmdStore(db, configManager,
        documentRepo, maintenanceRepo, statusRepo, cacheRepo, configSync,
        options.LlmService);
    return Task.FromResult<IQmdStore>(store);
}
```

#### Update QmdStore Convenience Constructors

The test-oriented constructors (`QmdStore(IQmdDatabase db)`, `QmdStore(IQmdDatabase db, string dbPath)`) should also create default repository instances internally. This preserves backward compatibility for tests:

```csharp
public QmdStore(IQmdDatabase db, string dbPath = ":memory:")
{
    Db = db;
    DbPath = dbPath;
    _documentRepo = new DocumentRepository(db);
    _maintenanceRepo = new MaintenanceRepository(db);
    // ... etc
    _configManager = new ConfigManager(new InlineConfigSource(new CollectionConfig()));
    SchemaInitializer.Initialize(Db);
    VecExtension.TryLoad(Db);
}
```

#### Files Modified (5a)

- `src/Qmd.Core/Documents/DocumentOperations.cs` -> rename to `DocumentRepository.cs`, add interface
- `src/Qmd.Core/Documents/IDocumentRepository.cs` (NEW)
- `src/Qmd.Core/Indexing/MaintenanceOperations.cs` -> rename to `MaintenanceRepository.cs`, add interface
- `src/Qmd.Core/Indexing/IMaintenanceRepository.cs` (NEW)
- `src/Qmd.Core/Indexing/StatusOperations.cs` -> rename to `StatusRepository.cs`, add interface
- `src/Qmd.Core/Indexing/IStatusRepository.cs` (NEW)
- `src/Qmd.Core/Embedding/EmbeddingOperations.cs` -> rename to `EmbeddingRepository.cs`, add interface
- `src/Qmd.Core/Embedding/IEmbeddingRepository.cs` (NEW)
- `src/Qmd.Core/Indexing/CacheOperations.cs` -> rename to `CacheRepository.cs`, add interface
- `src/Qmd.Core/Indexing/ICacheRepository.cs` (NEW)
- `src/Qmd.Core/Configuration/ConfigSync.cs` -> rename to `ConfigSyncService.cs`, add interface
- `src/Qmd.Core/Configuration/IConfigSyncService.cs` (NEW)
- `src/Qmd.Core/Store/QmdStore.cs` -- inject repositories
- `src/Qmd.Core/QmdStoreFactory.cs` -- compose repositories
- `src/Qmd.Core/Content/ContentHasher.cs` -- remove `InsertContent` (moved to DocumentRepository)

#### Tests Affected (5a)

- `tests/Qmd.Core.Tests/Documents/DocumentOperationsTests.cs` -- update to use `new DocumentRepository(db)` instead of `DocumentOperations.Method(db, ...)`
- `tests/Qmd.Core.Tests/Indexing/CacheOperationsTests.cs` -- similar
- `tests/Qmd.Core.Tests/Indexing/StatusOperationsTests.cs` -- similar
- `tests/Qmd.Core.Tests/Embedding/EmbeddingOperationsTests.cs` -- similar
- `tests/Qmd.Core.Tests/Configuration/ConfigSyncTests.cs` -- similar
- Any test that calls `ContentHasher.InsertContent` -- update to use `DocumentRepository`
- `tests/Qmd.Core.Tests/Store/QmdStoreSdkTests.cs` -- update `Seed` methods

---

### Phase 5b: Search Services

Convert the search pipeline static classes. This is the most complex sub-phase because it must break the circular dependency between `QmdStore` and `HybridQueryService`/`StructuredSearchService`.

#### The Circular Dependency Problem

Currently:
- `HybridQueryService.HybridQueryAsync(QmdStore store, ...)` calls `store.SearchFTS()` and `store.SearchVecAsync()`
- `QmdStore.SearchAsync()` calls `HybridQueryService.HybridQueryAsync(this, ...)`

After conversion, `HybridQueryService` must depend on `IFtsSearchService` and `IVectorSearchService` (not on `QmdStore`).

#### New Interfaces

**`src/Qmd.Core/Search/IFtsSearchService.cs`**:
```csharp
internal interface IFtsSearchService
{
    List<SearchResult> Search(string query, int limit = 20, List<string>? collections = null);
}
```

**`src/Qmd.Core/Search/IVectorSearchService.cs`**:
```csharp
internal interface IVectorSearchService
{
    Task<List<SearchResult>> SearchAsync(string query, string model,
        int limit = 20, List<string>? collections = null,
        float[]? precomputedEmbedding = null, CancellationToken ct = default);
    bool IsVectorTableAvailable();
}
```

**`src/Qmd.Core/Search/IQueryExpanderService.cs`**:
```csharp
internal interface IQueryExpanderService
{
    Task<List<ExpandedQuery>> ExpandQueryAsync(string query, string? model, string? intent, CancellationToken ct);
}
```

**`src/Qmd.Core/Search/IRerankerService.cs`**:
```csharp
internal interface IRerankerService
{
    Task<List<(string File, double Score)>> RerankAsync(
        string query, List<RerankDocument> documents,
        string? model, string? intent, CancellationToken ct);
}
```

**`src/Qmd.Core/Search/IHybridQueryService.cs`**:
```csharp
internal interface IHybridQueryService
{
    Task<List<HybridQueryResult>> HybridQueryAsync(string query, HybridQueryOptions? options, CancellationToken ct);
}
```

**`src/Qmd.Core/Search/IStructuredSearchService.cs`**:
```csharp
internal interface IStructuredSearchService
{
    Task<List<HybridQueryResult>> SearchAsync(List<ExpandedQuery> searches, StructuredSearchOptions? options, CancellationToken ct);
}
```

#### Converted Classes

**`FtsSearcher` -> `FtsSearchService : IFtsSearchService`**:
- Constructor: `FtsSearchService(IQmdDatabase db, IContextResolverService contextResolver)`
- Remove `IQmdDatabase db` parameter from `SearchFTS`

**`VectorSearcher` -> `VectorSearchService : IVectorSearchService`**:
- Constructor: `VectorSearchService(IQmdDatabase db, IContextResolverService contextResolver)`
- Add `IsVectorTableAvailable()` method (replace inline sqlite_master check)

**`QueryExpander` -> `QueryExpanderService : IQueryExpanderService`**:
- Constructor: `QueryExpanderService(ICacheRepository cacheRepo, ILlmService llmService)`
- Remove `IQmdDatabase db` and `ILlmService` params from methods

**`Reranker` -> `RerankerService : IRerankerService`**:
- Constructor: `RerankerService(ICacheRepository cacheRepo, ILlmService llmService)`

**`HybridQueryService` -> `HybridQueryService : IHybridQueryService`** (drop `static`):
- Constructor: `HybridQueryService(IFtsSearchService fts, IVectorSearchService vec, IQueryExpanderService expander, IRerankerService reranker, IContextResolverService contextResolver, ILlmService llmService)`
- Remove `QmdStore store` and `ILlmService llmService` params from `HybridQueryAsync`

**`StructuredSearchService` -> `StructuredSearchService : IStructuredSearchService`** (drop `static`):
- Same pattern as HybridQueryService

#### Update QmdStore

```csharp
internal class QmdStore : IQmdStore, IDisposable
{
    private readonly IHybridQueryService _hybridQuery;
    private readonly IStructuredSearchService _structuredSearch;
    private readonly IFtsSearchService _ftsSearch;
    private readonly IVectorSearchService _vectorSearch;
    private readonly IQueryExpanderService _queryExpander;
    // ...

    public async Task<List<HybridQueryResult>> SearchAsync(SearchOptions options, CancellationToken ct)
    {
        return await _hybridQuery.HybridQueryAsync(options.Query, /* map options */, ct);
    }
}
```

The `SearchFTS()` and `SearchVecAsync()` methods on `QmdStore` become simple delegations to the injected services (or can be removed from QmdStore if only used by HybridQueryService, which now gets them directly).

#### Files Modified (5b)

All files in `src/Qmd.Core/Search/` (11 files) plus QmdStore and QmdStoreFactory.

#### Tests Affected (5b)

- `tests/Qmd.Core.Tests/Search/FtsSearcherTests.cs` -- `new FtsSearchService(db, contextResolver)` instead of `FtsSearcher.SearchFTS(db, ...)`
- `tests/Qmd.Core.Tests/Search/VectorSearcherTests.cs` -- similar
- `tests/Qmd.Core.Tests/Search/QueryExpanderTests.cs` -- similar
- `tests/Qmd.Core.Tests/Search/RerankerTests.cs` -- similar
- `tests/Qmd.Core.Tests/Search/HybridQueryTests.cs` -- most complex: must construct service graph or use mocks
- `tests/Qmd.Core.Tests/Search/StructuredSearchTests.cs` -- similar
- `tests/Qmd.Core.Tests/Search/EmbeddingProfilerTests.cs` -- similar

---

### Phase 5c: Retrieval and Indexing Services

#### New Interfaces

**`src/Qmd.Core/Retrieval/IDocumentFinderService.cs`**:
```csharp
internal interface IDocumentFinderService
{
    FindDocumentResult FindDocument(string filename, bool includeBody = false, int similarFilesLimit = 5);
    string? GetDocumentBody(string filepath, int? fromLine = null, int? maxLines = null);
}
```

**`src/Qmd.Core/Retrieval/IMultiGetService.cs`**:
```csharp
internal interface IMultiGetService
{
    (List<MultiGetResult> Docs, List<string> Errors) FindDocuments(string pattern, bool includeBody, int maxBytes);
}
```

**`src/Qmd.Core/Retrieval/IContextResolverService.cs`**:
```csharp
internal interface IContextResolverService
{
    string? GetContextForFile(string filepath);
}
```

**`src/Qmd.Core/Retrieval/IFuzzyMatcherService.cs`**:
```csharp
internal interface IFuzzyMatcherService
{
    List<string> FindSimilarFiles(string query, int maxDistance = 3, int limit = 5);
}
```

**`src/Qmd.Core/Indexing/ICollectionReindexerService.cs`**:
```csharp
internal interface ICollectionReindexerService
{
    Task<ReindexResult> ReindexCollectionAsync(string collectionPath, string globPattern,
        string collectionName, ReindexOptions? options = null);
}
```

**`src/Qmd.Core/Embedding/IEmbeddingPipelineService.cs`**:
```csharp
internal interface IEmbeddingPipelineService
{
    Task<EmbedResult> GenerateEmbeddingsAsync(EmbedPipelineOptions? options = null, Action<int>? ensureVecTable = null);
}
```

#### Key Design Decisions

1. **`CollectionReindexer`** currently takes `QmdStore` to access `store.Db`, `DocumentOperations`, `ContentHasher.InsertContent`, and `MaintenanceOperations`. After conversion, it takes `IDocumentRepository`, `IMaintenanceRepository`, and `IQmdDatabase` (for schema-level ops only if needed).

2. **`DocumentFinder`** calls `FuzzyMatcher.FindSimilarFiles` and `ContextResolver.GetContextForFile`. After conversion, it takes `IFuzzyMatcherService` and `IContextResolverService`.

3. **`EmbeddingPipeline`** calls `EmbeddingOperations` methods. After conversion, it takes `IEmbeddingRepository`.

#### Files Modified (5c)

All files in `src/Qmd.Core/Retrieval/` (5 files), `src/Qmd.Core/Indexing/CollectionReindexer.cs`, `src/Qmd.Core/Embedding/EmbeddingPipeline.cs`, plus QmdStore and QmdStoreFactory.

#### Tests Affected (5c)

- `tests/Qmd.Core.Tests/Retrieval/DocumentFinderTests.cs`
- `tests/Qmd.Core.Tests/Retrieval/ContextResolverTests.cs`
- `tests/Qmd.Core.Tests/Retrieval/FuzzyMatcherTests.cs`
- `tests/Qmd.Core.Tests/Retrieval/GlobMatcherTests.cs`
- `tests/Qmd.Core.Tests/Indexing/CollectionReindexerTests.cs`
- `tests/Qmd.Core.Tests/Embedding/EmbeddingPipelineTests.cs`

---

### Phase 5d: Wire Up DI in QmdStore and Factory

Final wiring: update `QmdStoreFactory` to compose the complete object graph.

```csharp
public static Task<IQmdStore> CreateAsync(StoreOptions options)
{
    // ... existing validation and config ...
    var db = new SqliteDatabase(options.DbPath);
    SchemaInitializer.Initialize(db);
    VecExtension.TryLoad(db);

    // Repositories
    var documentRepo = new DocumentRepository(db);
    var maintenanceRepo = new MaintenanceRepository(db);
    var statusRepo = new StatusRepository(db);
    var embeddingRepo = new EmbeddingRepository(db);
    var cacheRepo = new CacheRepository(db);
    var configSync = new ConfigSyncService(db);

    // Services
    var contextResolver = new ContextResolverService(db);
    var fuzzyMatcher = new FuzzyMatcherService(db);
    var documentFinder = new DocumentFinderService(db, fuzzyMatcher, contextResolver);
    var multiGet = new MultiGetService(db, documentFinder);
    var ftsSearch = new FtsSearchService(db, contextResolver);
    var vectorSearch = new VectorSearchService(db, contextResolver);

    IQueryExpanderService? queryExpander = null;
    IRerankerService? reranker = null;
    IHybridQueryService? hybridQuery = null;
    IStructuredSearchService? structuredSearch = null;

    if (options.LlmService != null)
    {
        queryExpander = new QueryExpanderService(cacheRepo, options.LlmService);
        reranker = new RerankerService(cacheRepo, options.LlmService);
        hybridQuery = new HybridQueryService(ftsSearch, vectorSearch, queryExpander, reranker, contextResolver, options.LlmService);
        structuredSearch = new StructuredSearchService(ftsSearch, vectorSearch, reranker, contextResolver, options.LlmService);
    }

    var store = new QmdStore(db, configManager, /* all services... */);
    return Task.FromResult<IQmdStore>(store);
}
```

**Note**: Consider using a simple service container or a builder pattern if the constructor gets too large. But manual composition is preferred for now -- it keeps dependencies explicit and avoids framework overhead.

#### QmdStore Constructor After Full Refactoring

The constructor will have many parameters. This is expected for a facade. Consider grouping into a `QmdStoreServices` record:

```csharp
internal record QmdStoreServices(
    IDocumentRepository DocumentRepo,
    IMaintenanceRepository MaintenanceRepo,
    IStatusRepository StatusRepo,
    IEmbeddingRepository EmbeddingRepo,
    ICacheRepository CacheRepo,
    IConfigSyncService ConfigSync,
    IFtsSearchService FtsSearch,
    IVectorSearchService VectorSearch,
    IHybridQueryService? HybridQuery,
    IStructuredSearchService? StructuredSearch,
    IDocumentFinderService DocumentFinder,
    IMultiGetService MultiGet,
    IContextResolverService ContextResolver,
    IFuzzyMatcherService FuzzyMatcher,
    ICollectionReindexerService CollectionReindexer,
    IEmbeddingPipelineService? EmbeddingPipeline
);
```

Then: `new QmdStore(db, configManager, services, llmService)`.

## Overall Risk Assessment

| Sub-Phase | Risk | Key Concern |
|-----------|------|-------------|
| 5a | MEDIUM | Constructor changes propagate to all test setup |
| 5b | MEDIUM-HIGH | Breaking QmdStore circular dependency requires careful interface design |
| 5c | MEDIUM | Many files, significant test updates |
| 5d | MEDIUM | Composition root complexity, testing convenience constructors |

## New Tests to Add

For each new interface, consider adding a test that constructs the service with a real in-memory database (integration-style). The goal isn't exhaustive mock testing -- it's ensuring the DI wiring works:

- `tests/Qmd.Core.Tests/Search/HybridQueryServiceIntegrationTests.cs` -- construct the full search pipeline with real services, run a query, verify results

## Dependencies

Must come after:
- Phase 1 (typed row models -- eliminates dictionary access before moving methods)
- Phase 3 (error handling -- clean error contracts before defining interfaces)
- Phase 4 (utility separation -- know which classes to convert)

## Verification

After each sub-phase:
1. `dotnet build Qmd.slnx -c Release` -- must compile cleanly
2. `dotnet test Qmd.slnx -c Release --filter "Category!=LLM"` -- all tests pass
3. Verify no remaining static service calls from QmdStore (grep for `ClassName.Method(Db,` patterns)
