using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Llm;
using Qmd.Core.Models;
using Qmd.Core.Search;
using Qmd.Core.Tests.Llm;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Search;

[Trait("Category", "Database")]
public class RerankerTests : IDisposable
{
    private readonly IQmdDatabase db;
    private readonly MockLlmService llm;
    private readonly RerankerService reranker;

    public RerankerTests()
    {
        this.db = TestDbHelper.CreateInMemoryDb();
        this.llm = new MockLlmService();
        this.reranker = new RerankerService(this.db, this.llm);
    }

    public void Dispose() => this.db.Dispose();

    [Fact]
    public async Task Rerank_ReturnsScores()
    {
        var docs = new List<RerankDocument>
        {
            new("file1.md", "Content about APIs"),
            new("file2.md", "Content about databases"),
        };
        var results = await this.reranker.RerankAsync("API", docs, ct: TestContext.Current.CancellationToken);
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task Rerank_EmptyDocs_ReturnsEmpty()
    {
        var results = await this.reranker.RerankAsync("query", [], ct: TestContext.Current.CancellationToken);
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
        var results = await this.reranker.RerankAsync("query", docs, ct: TestContext.Current.CancellationToken);
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r => r.File.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public async Task Rerank_CachesResults()
    {
        var scoringLlm = new ScoringMockLlmService();
        var reranker = new RerankerService(this.db, scoringLlm);
        var docs = new List<RerankDocument>
        {
            new("doc1.md", "Content for caching test"),
        };

        // First call
        await reranker.RerankAsync("cache test query", docs, ct: TestContext.Current.CancellationToken);
        // Second call — should hit cache
        var results = await reranker.RerankAsync("cache test query", docs, ct: TestContext.Current.CancellationToken);

        results.Should().HaveCount(1);
        scoringLlm.RerankCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Rerank_DeduplicatesIdenticalChunksAcrossFiles()
    {
        var scoringLlm = new ScoringMockLlmService();
        var reranker = new RerankerService(this.db, scoringLlm);
        var docs = new List<RerankDocument>
        {
            new("doc1.md", "Shared chunk text"),
            new("doc2.md", "Shared chunk text"),
        };

        var results = await reranker.RerankAsync("shared", docs, ct: TestContext.Current.CancellationToken);

        results.Should().HaveCount(2);
        scoringLlm.RerankCallCount.Should().Be(1);
        scoringLlm.LastRerankDocCount.Should().Be(1);
    }

    [Fact]
    public async Task Rerank_DedupMapsScoreBackToAllDuplicateFiles()
    {
        var scoringLlm = new ScoringMockLlmService();
        var reranker = new RerankerService(this.db, scoringLlm);
        var docs = new List<RerankDocument>
        {
            new("a.md", "shared chunk"),
            new("b.md", "shared chunk"),
            new("c.md", "different chunk"),
        };

        var results = await reranker.RerankAsync("query", docs, ct: TestContext.Current.CancellationToken);

        results.Should().HaveCount(3);
        scoringLlm.LastRerankDocCount.Should().Be(2);

        var scoreByFile = results.ToDictionary(r => r.File, r => r.Score);
        scoreByFile["a.md"].Should().Be(scoreByFile["b.md"]);
    }

    private class ScoringMockLlmService : ILlmService
    {
        public string EmbedModelName => "mock-scorer";
        public int RerankCallCount { get; private set; }
        public int LastRerankDocCount { get; private set; }

        public Task<RerankResult> RerankAsync(string query, List<RerankDocument> documents,
            RerankOptions? options = null, CancellationToken ct = default)
        {
            this.RerankCallCount++;
            this.LastRerankDocCount = documents.Count;
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
