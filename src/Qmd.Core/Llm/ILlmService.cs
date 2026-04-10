using Qmd.Core.Models;

namespace Qmd.Core.Llm;

/// <summary>
/// Core LLM abstraction. Implemented by LlamaSharpService in Qmd.Llm.
/// All methods are async with CancellationToken support.
/// </summary>
public interface ILlmService : IAsyncDisposable
{
    string EmbedModelName { get; }

    Task<EmbeddingResult?> EmbedAsync(string text, EmbedOptions? options = null, CancellationToken ct = default);
    Task<List<EmbeddingResult?>> EmbedBatchAsync(List<string> texts, EmbedOptions? options = null, CancellationToken ct = default);

    // Phase 4 stubs — define interface now, implement later
    Task<GenerateResult?> GenerateAsync(string prompt, GenerateOptions? options = null, CancellationToken ct = default);
    Task<RerankResult> RerankAsync(string query, List<RerankDocument> documents, RerankOptions? options = null, CancellationToken ct = default);
    Task<List<QueryExpansion>> ExpandQueryAsync(string query, ExpandQueryOptions? options = null, CancellationToken ct = default);

    int CountTokens(string text);
}
