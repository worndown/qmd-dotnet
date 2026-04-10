using Qmd.Core.Models;

namespace Qmd.Core.Llm;

/// <summary>
/// Core LLM abstraction. Implemented by LlamaSharpService in Qmd.Llm.
/// All methods are async with CancellationToken support.
/// </summary>
public interface ILlmService : IAsyncDisposable
{
    /// <summary>Name or URI of the active embedding model.</summary>
    string EmbedModelName { get; }

    /// <summary>Generate a vector embedding for a single text.</summary>
    Task<EmbeddingResult?> EmbedAsync(string text, EmbedOptions? options = null, CancellationToken ct = default);

    /// <summary>Generate vector embeddings for multiple texts in one batch.</summary>
    Task<List<EmbeddingResult?>> EmbedBatchAsync(List<string> texts, EmbedOptions? options = null, CancellationToken ct = default);

    /// <summary>Generic text completion. Not currently used by any command.</summary>
    Task<GenerateResult?> GenerateAsync(string prompt, GenerateOptions? options = null, CancellationToken ct = default);

    /// <summary>Score and reorder documents by relevance to a query.</summary>
    Task<RerankResult> RerankAsync(string query, List<RerankDocument> documents, RerankOptions? options = null, CancellationToken ct = default);

    /// <summary>Expand a search query into multiple typed search strategies (lex/vec/hyde).</summary>
    Task<List<QueryExpansion>> ExpandQueryAsync(string query, ExpandQueryOptions? options = null, CancellationToken ct = default);

    /// <summary>Count the number of tokens in a text string.</summary>
    int CountTokens(string text);
}
