using FluentAssertions;
using Qmd.Core.Chunking;
using Qmd.Core.Database;
using Qmd.Core.Llm;
using Qmd.Core.Models;
using Qmd.Core.Search;

namespace Qmd.Core.Tests.Llm;

/// <summary>
/// Integration tests that require real LlamaSharp models (GPU/CPU).
/// Run with: dotnet test --filter "Category=LLM"
/// Skip with: dotnet test --filter "Category!=LLM"
///
/// Port of TS test/llm.test.ts, test/store.test.ts:499-556, and test/eval.test.ts.
/// </summary>
[Trait("Category", "LLM")]
public class LlamaSharpIntegrationTests : IAsyncDisposable
{
    private LlamaSharpService? _llm;

    private LlamaSharpService GetLlm()
    {
        _llm ??= new LlamaSharpService();
        return _llm;
    }

    public async ValueTask DisposeAsync()
    {
        if (_llm != null) await _llm.DisposeAsync();
    }

    // =========================================================================
    // Embedding tests (port of llm.test.ts:200-300)
    // =========================================================================

    [Fact]
    public async Task Embed_GeneratesCorrectDimensions()
    {
        var llm = GetLlm();
        var result = await llm.EmbedAsync("Hello world");

        result.Should().NotBeNull();
        result!.Embedding.Should().HaveCount(768); // embeddinggemma-300M
    }

    [Fact]
    public async Task Embed_SameTextProducesSameEmbedding()
    {
        var llm = GetLlm();
        var result1 = await llm.EmbedAsync("Consistent embedding test");
        var result2 = await llm.EmbedAsync("Consistent embedding test");

        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1!.Embedding.Should().Equal(result2!.Embedding);
    }

    [Fact]
    public async Task Embed_DifferentTextsProduceDifferentEmbeddings()
    {
        var llm = GetLlm();
        var result1 = await llm.EmbedAsync("The cat sat on the mat");
        var result2 = await llm.EmbedAsync("Quantum physics and relativity");

        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1!.Embedding.Should().NotEqual(result2!.Embedding);
    }

    [Fact]
    public async Task EmbedBatch_ProducesSameResultsAsIndividual()
    {
        var llm = GetLlm();
        var texts = new List<string> { "First text", "Second text", "Third text" };

        var batchResults = await llm.EmbedBatchAsync(texts);
        var individualResults = new List<EmbeddingResult?>();
        foreach (var text in texts)
            individualResults.Add(await llm.EmbedAsync(text));

        batchResults.Should().HaveCount(3);
        for (int i = 0; i < 3; i++)
        {
            batchResults[i].Should().NotBeNull();
            individualResults[i].Should().NotBeNull();
            batchResults[i]!.Embedding.Should().Equal(individualResults[i]!.Embedding);
        }
    }

    // =========================================================================
    // Tokenization tests (port of store.test.ts:499-556)
    // =========================================================================

    [Fact]
    public async Task LlamaSharpTokenizer_CountsTokensAccurately()
    {
        var llm = GetLlm();
        // Force model load by embedding something first
        await llm.EmbedAsync("load model");

        var tokenCount = llm.CountTokens("Hello world, this is a test.");
        tokenCount.Should().BeGreaterThan(0);
        tokenCount.Should().BeLessThan(28); // Tokens < chars for English
    }

    [Fact]
    public async Task ChunkDocumentByTokens_WithRealTokenizer_StaysWithinLimits()
    {
        var llm = GetLlm();
        // Force model load
        await llm.EmbedAsync("load model");

        // Create a LlamaSharpTokenizer-like wrapper
        var tokenizer = new LlmServiceTokenizer(llm);
        var content = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 250));

        var chunks = DocumentChunker.ChunkDocumentByTokens(tokenizer, content, 900, 135);

        chunks.Count.Should().BeGreaterThan(1);
        foreach (var chunk in chunks)
        {
            chunk.Tokens.Should().BeLessThanOrEqualTo(950); // Allow slight overage
            chunk.Tokens.Should().BeGreaterThan(0);
        }
    }

    // =========================================================================
    // Query expansion tests (port of llm.test.ts + mcp.test.ts)
    // =========================================================================

    [Fact]
    public async Task ExpandQuery_ReturnsVariants()
    {
        var llm = GetLlm();
        var db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(db);

        var results = await QueryExpander.ExpandQueryAsync(db, llm, "machine learning algorithms");

        results.Should().NotBeEmpty();
        // Should have different query types
        results.Select(r => r.Type).Distinct().Count().Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task ExpandQuery_DiffersFromOriginal()
    {
        var llm = GetLlm();
        var db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(db);

        var query = "how to deploy to production";
        var results = await QueryExpander.ExpandQueryAsync(db, llm, query);

        // At least one expansion should differ from the original
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.Query != query);
    }

    // =========================================================================
    // Reranking tests (port of llm.test.ts)
    // =========================================================================

    [Fact]
    public async Task Rerank_ScoresRelevantHigher()
    {
        var llm = GetLlm();
        var db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(db);

        var documents = new List<RerankDocument>
        {
            new("relevant.md", "Machine learning is a subset of artificial intelligence that enables systems to learn from data."),
            new("irrelevant.md", "The recipe for chocolate cake requires flour, sugar, eggs, and cocoa powder."),
        };

        var results = await Reranker.RerankAsync(db, llm, "What is machine learning?", documents);

        results.Should().HaveCount(2);
        var relevantScore = results.First(r => r.File == "relevant.md").Score;
        var irrelevantScore = results.First(r => r.File == "irrelevant.md").Score;
        relevantScore.Should().BeGreaterThan(irrelevantScore);
    }

}
