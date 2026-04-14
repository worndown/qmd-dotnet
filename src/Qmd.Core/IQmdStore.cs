using Qmd.Core.Configuration;
using Qmd.Core.Models;

namespace Qmd.Core;

/// <summary>
/// Public interface for QMD store.
/// </summary>
public interface IQmdStore : IAsyncDisposable
{
    #region Search

    /// <summary>
    /// Hybrid search with query expansion and reranking (recommended).
    /// Combines BM25 full-text and vector similarity via Reciprocal Rank Fusion,
    /// then optionally reranks candidates with an LLM.
    /// </summary>
    /// <param name="options">Search query, limits, collection filters, and pipeline flags.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ranked results with scores, snippets, and optional explain traces.</returns>
    Task<List<HybridQueryResult>> SearchAsync(SearchOptions options, CancellationToken ct = default);

    /// <summary>
    /// Execute a structured (multi-leg) search built from explicit lex:/vec:/hyde: prefixes.
    /// Each <see cref="ExpandedQuery"/> is run independently; results are fused via RRF.
    /// </summary>
    Task<List<HybridQueryResult>> SearchStructuredAsync(List<ExpandedQuery> searches, StructuredSearchOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// BM25 full-text keyword search (no LLM). Fast, deterministic, and offline.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <param name="options">Limit and collection filters.</param>
    Task<List<SearchResult>> SearchLexAsync(string query, LexSearchOptions? options = null);

    /// <summary>
    /// Vector similarity search using pre-computed embeddings (no reranking).
    /// </summary>
    /// <param name="query">Natural-language query to embed and search.</param>
    /// <param name="options">Limit, min-score threshold, and collection filters.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<List<SearchResult>> SearchVectorAsync(string query, VectorSearchOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Expand a natural-language query into multiple search legs (lex, vec, hyde)
    /// using the query-expansion LLM.
    /// </summary>
    /// <param name="query">Natural-language query.</param>
    /// <param name="options">Optional intent context.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<List<ExpandedQuery>> ExpandQueryAsync(string query, ExpandQuerySdkOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Run the full hybrid search pipeline (BM25 + vector + RRF + reranking) with low-level options.
    /// For most use cases, prefer <see cref="SearchAsync"/> which wraps this with simpler options.
    /// </summary>
    /// <param name="query">Natural-language search query.</param>
    /// <param name="options">Full pipeline options including candidate limits, explain traces, and hooks.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<List<HybridQueryResult>> HybridQueryAsync(string query, HybridQueryOptions? options = null, CancellationToken ct = default);

    #endregion

    #region Retrieval

    /// <summary>
    /// Retrieve a single document by file path, virtual path, or doc id (<c>#abc123</c>).
    /// </summary>
    /// <param name="pathOrDocId">File path, <c>qmd://collection/path</c>, or <c>#docid</c>.</param>
    /// <param name="options">Whether to include the full body.</param>
    Task<FindDocumentResult> GetAsync(string pathOrDocId, GetOptions? options = null);

    /// <summary>
    /// Retrieve the raw body text of a document, with optional line slicing.
    /// </summary>
    /// <param name="pathOrDocId">File path, virtual path, or doc id.</param>
    /// <param name="options">Optional line range (<see cref="BodyOptions.FromLine"/>, <see cref="BodyOptions.MaxLines"/>).</param>
    /// <returns>The document body (or sliced portion), or <c>null</c> if not found.</returns>
    Task<string?> GetDocumentBodyAsync(string pathOrDocId, BodyOptions? options = null);

    /// <summary>
    /// Retrieve multiple documents by glob pattern or comma-separated list of paths/docids.
    /// </summary>
    /// <param name="pattern">
    /// A glob pattern (e.g. <c>docs/**/*.md</c>) or comma-separated list
    /// (e.g. <c>#abc123, #def456</c>).
    /// </param>
    /// <param name="options">Body inclusion flag and max-bytes threshold for skipping large files.</param>
    /// <returns>Matched documents and any errors for unresolved entries.</returns>
    Task<(List<MultiGetResult> Docs, List<string> Errors)> MultiGetAsync(string pattern, MultiGetOptions? options = null);

    /// <summary>
    /// List files in a collection, optionally filtered by a path prefix (SQL LIKE).
    /// </summary>
    /// <param name="collection">Collection name.</param>
    /// <param name="pathPrefix">Optional prefix to filter paths (e.g. <c>wiki/concepts</c>).</param>
    /// <returns>File entries sorted by path, with body length.</returns>
    Task<List<ListFileEntry>> ListFilesAsync(string collection, string? pathPrefix = null);

    /// <summary>
    /// Get the combined context string for a file path (hierarchical, most-specific-first).
    /// </summary>
    /// <param name="filepath">Absolute file path or virtual path (<c>qmd://collection/path</c>).</param>
    /// <returns>Combined context string, or <c>null</c> if no context is set.</returns>
    Task<string?> GetContextForFileAsync(string filepath);

    /// <summary>
    /// Find file paths similar to a query using Levenshtein distance ("did you mean?" suggestions).
    /// </summary>
    /// <param name="query">File path or partial path to match against.</param>
    /// <param name="maxDistance">Maximum edit distance (default 3).</param>
    /// <param name="limit">Maximum number of suggestions (default 5).</param>
    Task<List<string>> FindSimilarFilesAsync(string query, int maxDistance = 3, int limit = 5);

    /// <summary>
    /// List all active (non-deleted) document paths in a collection.
    /// </summary>
    /// <param name="collection">Collection name.</param>
    Task<List<string>> GetActiveDocumentPathsAsync(string collection);

    #endregion

    #region Collections

    /// <summary>
    /// Index a directory as a new collection. Scans files matching <paramref name="pattern"/>
    /// and inserts them into the database.
    /// </summary>
    /// <param name="name">Collection name.</param>
    /// <param name="path">Absolute path to the directory on disk.</param>
    /// <param name="pattern">Glob pattern for file matching (default <c>**/*.md</c>).</param>
    /// <param name="ignore">Optional glob patterns to exclude.</param>
    Task AddCollectionAsync(string name, string path, string pattern = "**/*.md", List<string>? ignore = null);

    /// <summary>
    /// Remove a collection and delete all its documents from the index.
    /// </summary>
    /// <returns><c>true</c> if the collection existed and was removed.</returns>
    Task<bool> RemoveCollectionAsync(string name);

    /// <summary>
    /// Rename a collection. Documents are updated to reference the new name.
    /// </summary>
    /// <returns><c>true</c> if the collection existed and was renamed.</returns>
    Task<bool> RenameCollectionAsync(string oldName, string newName);

    /// <summary>
    /// List all registered collections.
    /// </summary>
    Task<List<NamedCollection>> ListCollectionsAsync();

    /// <summary>
    /// Get names of collections included in default (unfiltered) searches.
    /// </summary>
    Task<List<string>> GetDefaultCollectionNamesAsync();

    /// <summary>
    /// Update collection settings: custom update command, include/exclude from defaults.
    /// </summary>
    /// <param name="name">Collection name.</param>
    /// <param name="update">Shell command to run before re-indexing (e.g. <c>git pull</c>).</param>
    /// <param name="includeByDefault">Whether to include in default searches.</param>
    /// <param name="clearUpdate">Set <c>true</c> to clear the update command.</param>
    Task<bool> UpdateCollectionSettingsAsync(string name, string? update = null, bool? includeByDefault = null, bool clearUpdate = false);

    #endregion

    #region Context

    /// <summary>
    /// Add descriptive context to a collection path. Context is included in search results
    /// and MCP tool responses to help LLMs understand the content.
    /// </summary>
    /// <param name="collection">Collection name.</param>
    /// <param name="pathPrefix">Path prefix (empty string for collection root).</param>
    /// <param name="text">Context description.</param>
    Task<bool> AddContextAsync(string collection, string pathPrefix, string text);

    /// <summary>
    /// Remove context from a collection path.
    /// </summary>
    Task<bool> RemoveContextAsync(string collection, string pathPrefix);

    /// <summary>
    /// Set or clear global context applied to all collections (system message).
    /// Pass <c>null</c> to clear.
    /// </summary>
    Task SetGlobalContextAsync(string? context);

    /// <summary>
    /// Get the global context string, or <c>null</c> if none is set.
    /// </summary>
    Task<string?> GetGlobalContextAsync();

    /// <summary>
    /// List all context entries across all collections.
    /// </summary>
    Task<List<(string Collection, string Path, string Context)>> ListContextsAsync();

    #endregion

    #region Indexing

    /// <summary>
    /// Re-index all (or specified) collections by scanning files on disk.
    /// Detects added, modified, and removed files.
    /// </summary>
    Task<ReindexResult> UpdateAsync(UpdateOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Generate vector embeddings for documents that need them.
    /// Uses the configured embedding model via LLamaSharp.
    /// </summary>
    Task<EmbedResult> EmbedAsync(EmbedPipelineOptions? options = null, CancellationToken ct = default);

    #endregion

    #region Diagnostics

    /// <summary>
    /// Profile the embedding model's similarity distribution on the indexed corpus.
    /// Samples random document chunks, searches for neighbors, and returns percentile
    /// statistics to help calibrate <c>--min-score</c> thresholds.
    /// </summary>
    /// <param name="options">Sample size and optional collection filter.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<EmbeddingProfile> ProfileEmbeddingsAsync(EmbeddingProfileOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Perform database maintenance: clear the LLM response cache, remove inactive and
    /// orphaned documents, clean up dangling content and vector rows, and vacuum the database.
    /// </summary>
    /// <param name="options">Which maintenance steps to run (all enabled by default).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<CleanupResult> CleanupAsync(CleanupOptions? options = null, CancellationToken ct = default);

    #endregion

    #region Health

    /// <summary>
    /// Get index status including document counts, collection info, and model paths.
    /// </summary>
    Task<IndexStatus> GetStatusAsync();

    /// <summary>
    /// Get index health details: total documents, embedding coverage, and vector index status.
    /// </summary>
    Task<IndexHealthInfo> GetIndexHealthAsync();

    #endregion
}

#region Specific option types

/// <summary>
/// Options for <see cref="IQmdStore.SearchAsync"/>: hybrid search with query expansion and reranking.
/// </summary>
public class SearchOptions
{
    /// <summary>Natural-language search query.</summary>
    public required string Query { get; init; }

    /// <summary>Restrict search to these collections. <c>null</c> searches all included collections.</summary>
    public List<string>? Collections { get; init; }

    /// <summary>Maximum number of results to return.</summary>
    public int Limit { get; init; } = 10;

    /// <summary>Minimum relevance score (0-1). Results below this threshold are excluded.</summary>
    public double MinScore { get; init; }

    /// <summary>Domain context hint for query expansion (e.g. "SharePoint architecture").</summary>
    public string? Intent { get; init; }

    /// <summary>Skip LLM reranking and use RRF fusion scores directly.</summary>
    public bool SkipRerank { get; init; }

    /// <summary>Maximum number of candidates to send to the reranker.</summary>
    public int CandidateLimit { get; init; } = 40;

    /// <summary>Chunking strategy for embedding: <c>Regex</c> (default) or <c>Auto</c> (AST for code files).</summary>
    public ChunkStrategy ChunkStrategy { get; init; } = ChunkStrategy.Regex;

    /// <summary>Include detailed retrieval traces (FTS/vector scores, RRF breakdown) in results.</summary>
    public bool Explain { get; init; }

    /// <summary>
    /// Pass a new instance to receive pipeline diagnostics (vector-only flag, best scores).
    /// Leave null to skip diagnostic collection.
    /// </summary>
    public HybridQueryDiagnostics? Diagnostics { get; set; }
}

/// <summary>
/// Options for <see cref="IQmdStore.SearchLexAsync"/>: BM25 full-text search.
/// </summary>
public class LexSearchOptions
{
    /// <summary>Maximum number of results to return.</summary>
    public int Limit { get; init; } = 20;

    /// <summary>Restrict search to these collections.</summary>
    public List<string>? Collections { get; init; }
}

/// <summary>
/// Options for <see cref="IQmdStore.SearchVectorAsync"/>: vector similarity search.
/// </summary>
public class VectorSearchOptions
{
    /// <summary>Maximum number of results to return.</summary>
    public int Limit { get; init; } = 20;

    /// <summary>Minimum cosine similarity score. Results below this are excluded.</summary>
    public double MinScore { get; init; } = 0;

    /// <summary>Restrict search to these collections.</summary>
    public List<string>? Collections { get; init; }

    /// <summary>Override the embedding model identifier.</summary>
    public string? Model { get; init; }

    /// <summary>Domain context hint for the embedding query.</summary>
    public string? Intent { get; init; }
}

/// <summary>
/// Options for <see cref="IQmdStore.ExpandQueryAsync"/>.
/// </summary>
public class ExpandQuerySdkOptions
{
    /// <summary>Domain context hint for query expansion.</summary>
    public string? Intent { get; init; }
}

/// <summary>
/// Options for <see cref="IQmdStore.GetAsync"/>.
/// </summary>
public class GetOptions
{
    /// <summary>Include the full document body in the result.</summary>
    public bool IncludeBody { get; init; }
}

/// <summary>
/// Options for <see cref="IQmdStore.GetDocumentBodyAsync"/>: optional line slicing.
/// </summary>
public class BodyOptions
{
    /// <summary>Start line (1-indexed). <c>null</c> starts from the beginning.</summary>
    public int? FromLine { get; init; }

    /// <summary>Maximum lines to return. <c>null</c> returns all remaining lines.</summary>
    public int? MaxLines { get; init; }
}

/// <summary>
/// Options for <see cref="IQmdStore.MultiGetAsync"/>.
/// </summary>
public class MultiGetOptions
{
    /// <summary>Include the full document body in each result.</summary>
    public bool IncludeBody { get; init; }

    /// <summary>Skip files larger than this byte count (default 10 KB).</summary>
    public int MaxBytes { get; init; } = 10 * 1024;
}

/// <summary>
/// A file entry returned by <see cref="IQmdStore.ListFilesAsync"/>.
/// </summary>
public class ListFileEntry
{
    /// <summary>Relative file path within the collection.</summary>
    public string Path { get; init; } = "";

    /// <summary>Display-friendly path (same as <see cref="Path"/> by default).</summary>
    public string DisplayPath { get; init; } = "";

    /// <summary>Size of the document body in bytes.</summary>
    public int BodyLength { get; init; }
}

/// <summary>
/// Options for <see cref="IQmdStore.UpdateAsync"/>: re-index collections.
/// </summary>
public class UpdateOptions
{
    /// <summary>Restrict re-indexing to these collections. <c>null</c> updates all.</summary>
    public List<string>? Collections { get; init; }

    /// <summary>Progress reporter invoked during re-indexing.</summary>
    public IProgress<ReindexProgress>? Progress { get; init; }
}

/// <summary>
/// Options for <see cref="IQmdStore.CleanupAsync"/>: which maintenance steps to perform.
/// All steps are enabled by default.
/// </summary>
public class CleanupOptions
{
    /// <summary>Clear the LLM response cache table.</summary>
    public bool DeleteCache { get; init; } = true;

    /// <summary>Delete soft-deleted (inactive) document records.</summary>
    public bool DeleteInactive { get; init; } = true;

    /// <summary>Remove documents whose collection no longer exists, plus orphaned content and vector rows.</summary>
    public bool CleanOrphans { get; init; } = true;

    /// <summary>Run SQLite VACUUM to reclaim disk space after deletions.</summary>
    public bool Vacuum { get; init; } = true;
}

#endregion
