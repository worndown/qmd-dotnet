using Qmd.Core.Llm;
using Qmd.Core.Models;

namespace Qmd.Core.Tests.Llm;

/// <summary>
/// Mock ILlmService for testing. Returns deterministic embeddings without real models.
/// </summary>
public class MockLlmService : ILlmService
{
    public string EmbedModelName => "mock-embed-model";
    public int EmbedCallCount { get; private set; }
    public int EmbedBatchCallCount { get; private set; }

    /// <summary>Embedding dimension returned by the mock.</summary>
    public int EmbedDimension { get; set; } = 3;

    /// <summary>Records the EmbedOptions passed to each EmbedAsync call.</summary>
    public List<EmbedOptions?> EmbedOptionsCalls { get; } = [];

    /// <summary>Records the EmbedOptions passed to each EmbedBatchAsync call.</summary>
    public List<EmbedOptions?> EmbedBatchOptionsCalls { get; } = [];

    /// <summary>Records the batch sizes passed to each EmbedBatchAsync call.</summary>
    public List<int> EmbedBatchSizes { get; } = [];

    public Task<EmbeddingResult?> EmbedAsync(string text, EmbedOptions? options = null, CancellationToken ct = default)
    {
        this.EmbedCallCount++;
        this.EmbedOptionsCalls.Add(options);
        var modelName = options?.Model ?? this.EmbedModelName;
        var embedding = new float[this.EmbedDimension];
        // Deterministic: hash-based embedding
        var hash = text.GetHashCode();
        for (int i = 0; i < this.EmbedDimension; i++)
            embedding[i] = (float)((hash + i) % 1000) / 1000f;
        return Task.FromResult<EmbeddingResult?>(new EmbeddingResult(embedding, modelName));
    }

    public Task<List<EmbeddingResult?>> EmbedBatchAsync(List<string> texts, EmbedOptions? options = null, CancellationToken ct = default)
    {
        this.EmbedBatchCallCount++;
        this.EmbedBatchOptionsCalls.Add(options);
        this.EmbedBatchSizes.Add(texts.Count);
        var modelName = options?.Model ?? this.EmbedModelName;
        var results = new List<EmbeddingResult?>();
        foreach (var text in texts)
        {
            var embedding = new float[this.EmbedDimension];
            var hash = text.GetHashCode();
            for (int i = 0; i < this.EmbedDimension; i++)
                embedding[i] = (float)((hash + i) % 1000) / 1000f;
            results.Add(new EmbeddingResult(embedding, modelName));
        }
        return Task.FromResult(results);
    }

    public int CountTokens(string text) => (int)Math.Ceiling(text.Length / 4.0);

    public Task<GenerateResult?> GenerateAsync(string prompt, GenerateOptions? options = null, CancellationToken ct = default)
        => Task.FromResult<GenerateResult?>(null);

    public Task<RerankResult> RerankAsync(string query, List<RerankDocument> documents, RerankOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new RerankResult([], "mock"));

    public Task<List<QueryExpansion>> ExpandQueryAsync(string query, ExpandQueryOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new List<QueryExpansion>
        {
            new(QueryType.Lex, $"{query} expanded"),
            new(QueryType.Vec, $"information about {query}"),
        });

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
