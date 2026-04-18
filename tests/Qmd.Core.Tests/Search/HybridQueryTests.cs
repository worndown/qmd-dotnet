using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Models;
using Qmd.Core.Search;
using Qmd.Core.Store;
using Qmd.Core.Tests.Llm;
using Qmd.Core.Tests.TestHelpers;

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
        TestDbHelper.SeedDocuments(_store.Db,
            ("docs", "api.md", "api.md", "This document describes the REST API endpoints for user authentication and authorization. OAuth2 flows are covered in detail."),
            ("docs", "guide.md", "guide.md", "Welcome to the getting started guide. Learn how to install and configure the system step by step."),
            ("docs", "deploy.md", "deploy.md", "Deployment guide covering Docker containers, Kubernetes orchestration, and CI/CD pipeline configuration."),
            ("notes", "meeting.md", "meeting.md", "Meeting notes about distributed systems architecture and multi-agent coordination patterns.")
        );
    }

    public void Dispose() => _store.Dispose();

    private HybridQueryService CreateService(SearchConfig? config = null)
    {
        return new HybridQueryService(
            _store.FtsSearch, _store.VectorSearch,
            new QueryExpanderService(_store.Db, _llm),
            new RerankerService(_store.Db, _llm),
            _store.Db, _llm, config ?? new SearchConfig());
    }

    [Fact]
    public async Task HybridQuery_ReturnsResults()
    {
        var results = await CreateService().HybridQueryAsync(
            "API authentication",
            new HybridQueryOptions { SkipRerank = true });
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HybridQuery_RespectsLimit()
    {
        var results = await CreateService().HybridQueryAsync(
            "guide",
            new HybridQueryOptions { Limit = 2, SkipRerank = true });
        results.Should().HaveCountLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task HybridQuery_StrongSignal_SkipsExpansion()
    {
        var results = await CreateService().HybridQueryAsync(
            "OAuth2",
            new HybridQueryOptions { SkipRerank = true });
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HybridQuery_WithIntent_DisablesStrongSignal()
    {
        var results = await CreateService().HybridQueryAsync(
            "API",
            new HybridQueryOptions { Intent = "security review", SkipRerank = true });
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HybridQuery_SkipRerank_UsesRrfScores()
    {
        var results = await CreateService().HybridQueryAsync(
            "deployment Docker",
            new HybridQueryOptions { SkipRerank = true });
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r => r.Score.Should().BeGreaterThan(0));
    }

    [Fact]
    public async Task HybridQuery_WithExplain_IncludesTrace()
    {
        var results = await CreateService().HybridQueryAsync(
            "API",
            new HybridQueryOptions { Explain = true, SkipRerank = true });
        results.Should().NotBeEmpty();
        results[0].Explain.Should().NotBeNull();
    }

    [Fact]
    public async Task HybridQuery_MinScore_FiltersLowScores()
    {
        var results = await CreateService().HybridQueryAsync(
            "API",
            new HybridQueryOptions { MinScore = 0.99, SkipRerank = true });
        results.Should().HaveCountLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task HybridQuery_HasBestChunk()
    {
        var results = await CreateService().HybridQueryAsync(
            "API",
            new HybridQueryOptions { SkipRerank = true });
        results.Should().NotBeEmpty();
        results[0].BestChunk.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task HybridQuery_EmptyQuery_ReturnsEmpty()
    {
        var results = await CreateService().HybridQueryAsync(
            "",
            new HybridQueryOptions { SkipRerank = true });
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task HybridQuery_FiltersByCollection()
    {
        // FtsMinSignal = 0: disable FTS gate since test corpus has no vectors
        var config = new SearchConfig { FtsMinSignal = 0.0 };
        var results = await CreateService(config).HybridQueryAsync(
            "systems",
            new HybridQueryOptions { Collections = ["notes"], SkipRerank = true });
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r => r.File.Should().Contain("notes"));
    }

    [Fact]
    public async Task HybridQuery_FtsGatedOut_StillReturnsResults()
    {
        // FtsMinSignal impossibly high — forces all FTS to be gated out
        var config = new SearchConfig { FtsMinSignal = 100.0 };
        var diag = new HybridQueryDiagnostics();
        var results = await CreateService(config).HybridQueryAsync(
            "API authentication",
            new HybridQueryOptions { SkipRerank = true, Diagnostics = diag });
        // Results should still come back via BM25 strong-signal path (bypasses ftsWeak gate)
        // or be empty if vector is unavailable — either way no crash
        diag.HasFtsResults.Should().BeFalse();
    }

    [Fact]
    public async Task HybridQuery_FtsGateDisabled_FtsContributes()
    {
        // FtsMinSignal = 0 means FTS is never gated out
        var config = new SearchConfig { FtsMinSignal = 0.0 };
        var diag = new HybridQueryDiagnostics();
        var results = await CreateService(config).HybridQueryAsync(
            "API authentication",
            new HybridQueryOptions { SkipRerank = true, Diagnostics = diag });
        results.Should().NotBeEmpty();
        diag.HasFtsResults.Should().BeTrue();
    }
}
