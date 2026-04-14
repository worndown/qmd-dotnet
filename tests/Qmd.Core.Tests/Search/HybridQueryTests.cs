using FluentAssertions;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Models;
using Qmd.Core.Search;
using Qmd.Core.Store;
using Qmd.Core.Tests.Llm;

namespace Qmd.Core.Tests.Search;

[Trait("Category", "Database")]
public class HybridQueryTests : IDisposable
{
    private readonly QmdStore _store;
    private readonly MockLlmService _llm;

    public HybridQueryTests()
    {
        _store = new QmdStore(new SqliteDatabase(":memory:"));
        _llm = new MockLlmService();
        _store.LlmService = _llm;
        SeedDocuments();
    }

    public void Dispose() => _store.Dispose();

    private void SeedDocuments()
    {
        void Seed(string collection, string path, string content)
        {
            var hash = ContentHasher.HashContent(content);
            ContentHasher.InsertContent(_store.Db, hash, content, "2025-01-01");
            DocumentOperations.InsertDocument(_store.Db, collection, path, path, hash, "2025-01-01", "2025-01-01");
        }

        Seed("docs", "api.md", "This document describes the REST API endpoints for user authentication and authorization. OAuth2 flows are covered in detail.");
        Seed("docs", "guide.md", "Welcome to the getting started guide. Learn how to install and configure the system step by step.");
        Seed("docs", "deploy.md", "Deployment guide covering Docker containers, Kubernetes orchestration, and CI/CD pipeline configuration.");
        Seed("notes", "meeting.md", "Meeting notes about distributed systems architecture and multi-agent coordination patterns.");
    }

    [Fact]
    public async Task HybridQuery_ReturnsResults()
    {
        var results = await HybridQueryService.HybridQueryAsync(
            _store, _llm, "API authentication",
            new HybridQueryOptions { SkipRerank = true });
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HybridQuery_RespectsLimit()
    {
        var results = await HybridQueryService.HybridQueryAsync(
            _store, _llm, "guide",
            new HybridQueryOptions { Limit = 2, SkipRerank = true });
        results.Should().HaveCountLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task HybridQuery_StrongSignal_SkipsExpansion()
    {
        // With a very specific query that should produce a strong signal
        var results = await HybridQueryService.HybridQueryAsync(
            _store, _llm, "OAuth2",
            new HybridQueryOptions { SkipRerank = true });
        // Should still return results (via direct FTS)
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HybridQuery_WithIntent_DisablesStrongSignal()
    {
        var results = await HybridQueryService.HybridQueryAsync(
            _store, _llm, "API",
            new HybridQueryOptions { Intent = "security review", SkipRerank = true });
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HybridQuery_SkipRerank_UsesRrfScores()
    {
        var results = await HybridQueryService.HybridQueryAsync(
            _store, _llm, "deployment Docker",
            new HybridQueryOptions { SkipRerank = true });
        results.Should().NotBeEmpty();
        // All scores should be > 0
        results.Should().AllSatisfy(r => r.Score.Should().BeGreaterThan(0));
    }

    [Fact]
    public async Task HybridQuery_WithExplain_IncludesTrace()
    {
        var results = await HybridQueryService.HybridQueryAsync(
            _store, _llm, "API",
            new HybridQueryOptions { Explain = true, SkipRerank = true });
        results.Should().NotBeEmpty();
        results[0].Explain.Should().NotBeNull();
    }

    [Fact]
    public async Task HybridQuery_MinScore_FiltersLowScores()
    {
        var results = await HybridQueryService.HybridQueryAsync(
            _store, _llm, "API",
            new HybridQueryOptions { MinScore = 0.99, SkipRerank = true });
        // Very high min score should filter most/all results
        results.Should().HaveCountLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task HybridQuery_HasBestChunk()
    {
        var results = await HybridQueryService.HybridQueryAsync(
            _store, _llm, "API",
            new HybridQueryOptions { SkipRerank = true });
        results.Should().NotBeEmpty();
        results[0].BestChunk.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task HybridQuery_EmptyQuery_ReturnsEmpty()
    {
        var results = await HybridQueryService.HybridQueryAsync(
            _store, _llm, "",
            new HybridQueryOptions { SkipRerank = true });
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task HybridQuery_FiltersByCollection()
    {
        var results = await HybridQueryService.HybridQueryAsync(
            _store, _llm, "systems",
            new HybridQueryOptions { Collections = ["notes"], SkipRerank = true });
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r => r.File.Should().Contain("notes"));
    }
}
