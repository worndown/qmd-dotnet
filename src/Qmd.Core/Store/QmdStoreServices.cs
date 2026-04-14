using Qmd.Core.Configuration;
using Qmd.Core.Documents;
using Qmd.Core.Embedding;
using Qmd.Core.Indexing;
using Qmd.Core.Search;

namespace Qmd.Core.Store;

/// <summary>
/// Groups all service dependencies for <see cref="QmdStore"/>.
/// Used to simplify constructor wiring and factory composition.
/// </summary>
internal record QmdStoreServices(
    IDocumentRepository DocumentRepo,
    IMaintenanceRepository MaintenanceRepo,
    IStatusRepository StatusRepo,
    IEmbeddingRepository EmbeddingRepo,
    ICacheRepository CacheRepo,
    IConfigSyncService ConfigSync,
    IFtsSearchService FtsSearch,
    IVectorSearchService VectorSearch
);
