using FluentAssertions;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Llm;
using Qmd.Core.Models;
using Qmd.Core.Search;
using Qmd.Core.Store;

namespace Qmd.Core.Tests.Search;

public class StructuredSearchTests : IDisposable
{
    private readonly QmdStore _store;
    private readonly MockLlmService _llm = new();

    public StructuredSearchTests()
    {
        _store = new QmdStore(new SqliteDatabase(":memory:"));
        _store.LlmService = _llm;
        SeedDocuments();
    }

    public void Dispose() => _store.Dispose();

    private void SeedDocuments()
    {
        void Seed(string collection, string path, string title, string content)
        {
            var hash = ContentHasher.HashContent(content);
            ContentHasher.InsertContent(_store.Db, hash, content, "2025-01-01");
            DocumentOperations.InsertDocument(_store.Db, collection, path, title, hash, "2025-01-01", "2025-01-01");
        }

        Seed("docs", "api.md", "API Reference", "This document describes the REST API endpoints for authentication.");
        Seed("docs", "guide.md", "Getting Started", "Welcome to the getting started guide. Install and configure the system.");
        Seed("code", "auth.ts", "Auth Module", "OAuth2 authentication flow for users and service accounts.");
        Seed("code", "server.ts", "Server Setup", "Express server with middleware for logging and error handling.");
    }

    [Fact]
    public async Task StructuredSearch_WithLexQueries_ReturnsResults()
    {
        var searches = new List<ExpandedQuery>
        {
            new("lex", "API endpoints"),
        };

        var results = await StructuredSearchService.SearchAsync(_store, _llm, searches,
            new StructuredSearchOptions { SkipRerank = true });

        results.Should().NotBeEmpty();
        results[0].DisplayPath.Should().Contain("api.md");
    }

    [Fact]
    public async Task StructuredSearch_WithMultipleLexQueries()
    {
        var searches = new List<ExpandedQuery>
        {
            new("lex", "API endpoints"),
            new("lex", "authentication OAuth"),
        };

        var results = await StructuredSearchService.SearchAsync(_store, _llm, searches,
            new StructuredSearchOptions { SkipRerank = true });

        results.Should().NotBeEmpty();
        // Should find both API and auth docs via RRF fusion
        results.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task StructuredSearch_RespectsLimit()
    {
        var searches = new List<ExpandedQuery>
        {
            new("lex", "the"),
        };

        var results = await StructuredSearchService.SearchAsync(_store, _llm, searches,
            new StructuredSearchOptions { Limit = 2, SkipRerank = true });

        results.Should().HaveCountLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task StructuredSearch_ValidatesNewlines()
    {
        var searches = new List<ExpandedQuery>
        {
            new("lex", "query\nwith\nnewlines"),
        };

        var act = () => StructuredSearchService.SearchAsync(_store, _llm, searches);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*single-line*");
    }

    [Fact]
    public async Task StructuredSearch_EmptyResults_ReturnsEmptyList()
    {
        var searches = new List<ExpandedQuery>
        {
            new("lex", "xyznonexistent12345"),
        };

        var results = await StructuredSearchService.SearchAsync(_store, _llm, searches,
            new StructuredSearchOptions { SkipRerank = true });

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task StructuredSearch_FiltersCollections()
    {
        var searches = new List<ExpandedQuery>
        {
            new("lex", "authentication"),
        };

        var results = await StructuredSearchService.SearchAsync(_store, _llm, searches,
            new StructuredSearchOptions { Collections = ["docs"], SkipRerank = true });

        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r => r.File.Should().Contain("docs"));
    }

    [Fact]
    public async Task StructuredSearch_IncludesBestChunk()
    {
        var searches = new List<ExpandedQuery>
        {
            new("lex", "API endpoints"),
        };

        var results = await StructuredSearchService.SearchAsync(_store, _llm, searches,
            new StructuredSearchOptions { SkipRerank = true });

        results.Should().NotBeEmpty();
        results[0].BestChunk.Should().NotBeNullOrEmpty();
    }

    // =========================================================================
    // Edge cases
    // =========================================================================

    [Fact]
    public async Task StructuredSearch_InternalWhitespacePreserved()
    {
        // Internal whitespace is preserved in query
        var searches = new List<ExpandedQuery>
        {
            new("lex", "multiple   spaces   between"),
        };

        // Should not throw — whitespace is preserved in query
        var act = () => StructuredSearchService.SearchAsync(_store, _llm, searches,
            new StructuredSearchOptions { SkipRerank = true });
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StructuredSearch_ThrowsOnUnmatchedQuote()
    {
        // Throws when lex query has unmatched quote
        var searches = new List<ExpandedQuery>
        {
            new("lex", "unmatched \"quote here"),
        };

        var act = () => StructuredSearchService.SearchAsync(_store, _llm, searches,
            new StructuredSearchOptions { SkipRerank = true });
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*unmatched*quote*");
    }

    private class MockLlmService : ILlmService
    {
        public string EmbedModelName => "mock";

        public Task<EmbeddingResult?> EmbedAsync(string text, EmbedOptions? options = null, CancellationToken ct = default)
            => Task.FromResult<EmbeddingResult?>(null);

        public Task<List<EmbeddingResult?>> EmbedBatchAsync(List<string> texts, EmbedOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(texts.Select(_ => (EmbeddingResult?)null).ToList());

        public int CountTokens(string text) => (int)Math.Ceiling(text.Length / 4.0);

        public Task<GenerateResult?> GenerateAsync(string prompt, GenerateOptions? options = null, CancellationToken ct = default)
            => Task.FromResult<GenerateResult?>(null);

        public Task<RerankResult> RerankAsync(string query, List<RerankDocument> documents, RerankOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new RerankResult([], "mock"));

        public Task<List<QueryExpansion>> ExpandQueryAsync(string query, ExpandQueryOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new List<QueryExpansion> { new(QueryType.Lex, query) });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
