namespace Qmd.Core.Models;

public record CollectionInfo(string Name, string? Path, string? Pattern, int Documents, string LastUpdated);

public record IndexStatus(int TotalDocuments, int NeedsEmbedding, bool HasVectorIndex, List<CollectionInfo> Collections);

public record IndexHealthInfo(int NeedsEmbedding, int TotalDocs, int? DaysStale);

public record ReindexProgress(string File, int Current, int Total);

public record ReindexResult(int Indexed, int Updated, int Unchanged, int Removed, int OrphanedCleaned, int Collections = 0, int NeedsEmbedding = 0);

public record CleanupResult(
    int CacheEntriesDeleted,
    int InactiveDocsDeleted,
    int OrphanedCollectionDocsDeleted,
    int OrphanedContentDeleted,
    int OrphanedVectorsDeleted,
    bool Vacuumed);
