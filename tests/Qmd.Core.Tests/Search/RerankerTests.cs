using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Llm;
using Qmd.Core.Models;
using Qmd.Core.Search;
using Qmd.Core.Tests.Llm;

namespace Qmd.Core.Tests.Search;

public class RerankerTests : IDisposable
{
    private readonly SqliteDatabase _db;
    private readonly MockLlmService _llm;

    public RerankerTests()
    {
        _db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(_db);
        _llm = new MockLlmService();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Rerank_ReturnsScores()
    {
        var docs = new List<RerankDocument>
        {
            new("file1.md", "Content about APIs"),
            new("file2.md", "Content about databases"),
        };
        var results = await Reranker.RerankAsync(_db, _llm, "API", docs);
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task Rerank_EmptyDocs_ReturnsEmpty()
    {
        var results = await Reranker.RerankAsync(_db, _llm, "query", []);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Rerank_ReturnsScoresForAllDocs()
    {
        var docs = new List<RerankDocument>
        {
            new("a.md", "Content A"),
            new("b.md", "Content B"),
        };
        var results = await Reranker.RerankAsync(_db, _llm, "query", docs);
        // Mock returns empty results, so scores default to 0
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r => r.File.Should().NotBeNullOrEmpty());
    }

    // =========================================================================
    // rerank caches results
    // =========================================================================

    [Fact]
    public async Task Rerank_CachesResults()
    {
        // Ports: "rerank caches results" — call rerank twice with same inputs, verify cache hit
        var scoringLlm = new ScoringMockLlmService();
        var docs = new List<RerankDocument>
        {
            new("doc1.md", "Content for caching test"),
        };

        // First call
        await Reranker.RerankAsync(_db, scoringLlm, "cache test query", docs);
        // Second call — should hit cache
        var results = await Reranker.RerankAsync(_db, scoringLlm, "cache test query", docs);

        results.Should().HaveCount(1);
        // The LLM should have been called only once (second call hits cache)
        scoringLlm.RerankCallCount.Should().Be(1);
    }

    // =========================================================================
    // rerank deduplicates identical chunks across files
    // =========================================================================

    [Fact]
    public async Task Rerank_DeduplicatesIdenticalChunksAcrossFiles()
    {
        // Ports: "rerank deduplicates identical chunks across files"
        var scoringLlm = new ScoringMockLlmService();
        var docs = new List<RerankDocument>
        {
            new("doc1.md", "Shared chunk text"),
            new("doc2.md", "Shared chunk text"),
        };

        var results = await Reranker.RerankAsync(_db, scoringLlm, "shared", docs);

        // Both documents should get results
        results.Should().HaveCount(2);

        // The LLM rerank should only have been called once (identical text deduped)
        scoringLlm.RerankCallCount.Should().Be(1);
        // And the deduplicated call should only have 1 unique text
        scoringLlm.LastRerankDocCount.Should().Be(1);
    }

    // =========================================================================
    // "deduplicates identical document texts before scoring"
    // =========================================================================

    [Fact]
    public async Task Rerank_DedupMapsScoreBackToAllDuplicateFiles()
    {
        // TS: "deduplicates identical document texts before scoring"
        // 3 docs: 2 share "shared chunk" text, 1 has "different chunk"
        // rankAll should be called with only unique texts ["shared chunk", "different chunk"]
        // Both files with "shared chunk" should receive the same score.
        var scoringLlm = new ScoringMockLlmService();
        var docs = new List<RerankDocument>
        {
            new("a.md", "shared chunk"),
            new("b.md", "shared chunk"),
            new("c.md", "different chunk"),
        };

        var results = await Reranker.RerankAsync(_db, scoringLlm, "query", docs);

        results.Should().HaveCount(3);

        // The LLM should only receive 2 unique texts (deduped)
        scoringLlm.LastRerankDocCount.Should().Be(2);

        // Both "shared chunk" files should have the same score
        var scoreByFile = results.ToDictionary(r => r.File, r => r.Score);
        scoreByFile["a.md"].Should().Be(scoreByFile["b.md"]);
    }

    /// <summary>
    /// A mock LLM service that returns actual scores and tracks call counts.
    /// </summary>
    private class ScoringMockLlmService : ILlmService
    {
        public string EmbedModelName => "mock-scorer";
        public int RerankCallCount { get; private set; }
        public int LastRerankDocCount { get; private set; }

        public Task<RerankResult> RerankAsync(string query, List<RerankDocument> documents,
            RerankOptions? options = null, CancellationToken ct = default)
        {
            RerankCallCount++;
            LastRerankDocCount = documents.Count;
            var results = documents.Select((doc, index) => new RerankDocumentResult(
                doc.File, 1.0 - index * 0.1, index)).ToList();
            return Task.FromResult(new RerankResult(results, "mock-scorer"));
        }

        public Task<EmbeddingResult?> EmbedAsync(string text, EmbedOptions? options = null, CancellationToken ct = default)
            => Task.FromResult<EmbeddingResult?>(null);
        public Task<List<EmbeddingResult?>> EmbedBatchAsync(List<string> texts, EmbedOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new List<EmbeddingResult?>());
        public int CountTokens(string text) => text.Length / 4;
        public Task<GenerateResult?> GenerateAsync(string prompt, GenerateOptions? options = null, CancellationToken ct = default)
            => Task.FromResult<GenerateResult?>(null);
        public Task<List<QueryExpansion>> ExpandQueryAsync(string query, ExpandQueryOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new List<QueryExpansion>());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
