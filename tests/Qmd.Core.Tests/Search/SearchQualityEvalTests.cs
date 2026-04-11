using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Search;
using Qmd.Core.Store;

namespace Qmd.Core.Tests.Search;

/// <summary>
/// Search quality evaluation tests — verify BM25 ranking produces expected results.
/// </summary>
public class SearchQualityEvalTests : IDisposable
{
    private readonly QmdStore _store;

    public SearchQualityEvalTests()
    {
        _store = new QmdStore(new SqliteDatabase(":memory:"));
        SeedEvalDocuments();
    }

    public void Dispose() => _store.Dispose();

    private void SeedDoc(string collection, string path, string title, string content)
    {
        var hash = ContentHasher.HashContent(content);
        ContentHasher.InsertContent(_store.Db, hash, content, "2025-01-01");
        DocumentOperations.InsertDocument(_store.Db, collection, path, title, hash, "2025-01-01", "2025-01-01");
    }

    private void SeedEvalDocuments()
    {
        SeedDoc("docs", "api-design.md", "API Design Principles",
            "REST API design best practices including resource naming, HTTP methods, status codes, pagination, and error handling. " +
            "Use nouns for resources, not verbs. Prefer plural names. Use HTTP status codes correctly: 200 OK, 201 Created, 404 Not Found.");

        SeedDoc("docs", "distributed-systems.md", "Distributed Systems Architecture",
            "Consensus algorithms including Raft and Paxos for distributed state machines. " +
            "CAP theorem: choose between consistency, availability, and partition tolerance. " +
            "Event sourcing and CQRS patterns for microservices.");

        SeedDoc("docs", "fundraising.md", "Fundraising Strategy",
            "Series A fundraising checklist: pitch deck, financial model, cap table, due diligence documents. " +
            "Investor outreach strategies and term sheet negotiation. Valuation methods including DCF and comparable analysis.");

        SeedDoc("docs", "ml-deployment.md", "Machine Learning Deployment",
            "MLOps pipeline: model training, evaluation, versioning, deployment. " +
            "Feature stores, model registries, A/B testing for ML models. " +
            "GPU inference optimization and model compression techniques.");

        SeedDoc("docs", "security-auth.md", "Authentication & Authorization",
            "OAuth2 authorization code flow with PKCE. JWT token structure: header, payload, signature. " +
            "Role-based access control (RBAC) and attribute-based access control (ABAC). " +
            "Multi-factor authentication (MFA) implementation.");
    }

    // =========================================================================
    // Easy queries (exact keyword match)
    // =========================================================================

    [Fact]
    public void BM25_ExactKeyword_ApiDesign()
    {
        var results = FtsSearcher.SearchFTS(_store.Db, "REST API design");
        results.Should().NotBeEmpty();
        results[0].DisplayPath.Should().Contain("api-design");
    }

    [Fact]
    public void BM25_ExactKeyword_OAuth()
    {
        var results = FtsSearcher.SearchFTS(_store.Db, "OAuth2 JWT authentication");
        results.Should().NotBeEmpty();
        results[0].DisplayPath.Should().Contain("security-auth");
    }

    [Fact]
    public void BM25_ExactKeyword_Fundraising()
    {
        var results = FtsSearcher.SearchFTS(_store.Db, "Series A fundraising pitch deck");
        results.Should().NotBeEmpty();
        results[0].DisplayPath.Should().Contain("fundraising");
    }

    // =========================================================================
    // Medium queries (semantic but keyword-matchable)
    // =========================================================================

    [Fact]
    public void BM25_Medium_ConsensusAlgorithms()
    {
        var results = FtsSearcher.SearchFTS(_store.Db, "Raft Paxos consensus");
        results.Should().NotBeEmpty();
        results[0].DisplayPath.Should().Contain("distributed");
    }

    [Fact]
    public void BM25_Medium_ModelDeployment()
    {
        var results = FtsSearcher.SearchFTS(_store.Db, "MLOps model deployment GPU");
        results.Should().NotBeEmpty();
        results[0].DisplayPath.Should().Contain("ml-deployment");
    }

    // =========================================================================
    // Score quality checks
    // =========================================================================

    [Fact]
    public void BM25_Scores_Normalized_0_to_1()
    {
        var results = FtsSearcher.SearchFTS(_store.Db, "API");
        results.Should().NotBeEmpty();
        foreach (var r in results)
        {
            r.Score.Should().BeGreaterThan(0);
            r.Score.Should().BeLessThan(1);
        }
    }

    [Fact]
    public void BM25_Scores_OrderedDescending()
    {
        var results = FtsSearcher.SearchFTS(_store.Db, "deployment");
        if (results.Count > 1)
        {
            for (int i = 1; i < results.Count; i++)
                results[i].Score.Should().BeLessThanOrEqualTo(results[i - 1].Score);
        }
    }
}

/// <summary>
/// BM25 hit-rate evaluation tests.
/// Seeds the same 6 synthetic documents from eval-docs/ and runs queries
/// at different difficulty levels, asserting the same hit-rate thresholds.
/// </summary>
public class Bm25HitRateEvalTests : IDisposable
{
    private readonly QmdStore _store;

    // =========================================================================
    // Eval query fixtures
    // =========================================================================

    private record EvalQuery(string Query, string ExpectedDoc, string Difficulty);

    private static readonly EvalQuery[] EvalQueries =
    [
        // EASY: Exact keyword matches
        new("API versioning", "api-design", "easy"),
        new("Series A fundraising", "fundraising", "easy"),
        new("CAP theorem", "distributed-systems", "easy"),
        new("overfitting machine learning", "machine-learning", "easy"),
        new("remote work VPN", "remote-work", "easy"),
        new("Project Phoenix retrospective", "product-launch", "easy"),

        // MEDIUM: Semantic/conceptual queries
        new("how to structure REST endpoints", "api-design", "medium"),
        new("raising money for startup", "fundraising", "medium"),
        new("consistency vs availability tradeoffs", "distributed-systems", "medium"),
        new("how to prevent models from memorizing data", "machine-learning", "medium"),
        new("working from home guidelines", "remote-work", "medium"),
        new("what went wrong with the launch", "product-launch", "medium"),

        // HARD: Vague, partial memory, indirect
        new("nouns not verbs", "api-design", "hard"),
        new("Sequoia investor pitch", "fundraising", "hard"),
        new("Raft algorithm leader election", "distributed-systems", "hard"),
        new("F1 score precision recall", "machine-learning", "hard"),
        new("quarterly team gathering travel", "remote-work", "hard"),
        new("beta program 47 bugs", "product-launch", "hard"),

        // FUSION: Multi-signal queries (included in overall)
        new("how much runway before running out of money", "fundraising", "fusion"),
        new("datacenter replication sync strategy", "distributed-systems", "fusion"),
        new("splitting data for training and testing", "machine-learning", "fusion"),
        new("JSON response codes error messages", "api-design", "fusion"),
        new("video calls camera async messaging", "remote-work", "fusion"),
        new("CI/CD pipeline testing coverage", "product-launch", "fusion"),
    ];

    // =========================================================================
    // Setup — load and index eval documents (mirrors TS beforeAll)
    // =========================================================================

    public Bm25HitRateEvalTests()
    {
        _store = new QmdStore(new SqliteDatabase(":memory:"));

        // Locate eval-docs directory relative to the test assembly output
        var assemblyDir = Path.GetDirectoryName(typeof(Bm25HitRateEvalTests).Assembly.Location)!;
        var evalDocsDir = Path.Combine(assemblyDir, "Search", "EvalDocs");

        if (!Directory.Exists(evalDocsDir))
            throw new DirectoryNotFoundException($"Eval docs not found at {evalDocsDir}");

        var files = Directory.GetFiles(evalDocsDir, "*.md");
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);
            var title = content.Split('\n')[0]?.TrimStart('#', ' ') ?? fileName;
            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..12].ToLowerInvariant();
            var now = DateTime.UtcNow.ToString("o");

            ContentHasher.InsertContent(_store.Db, hash, content, now);
            DocumentOperations.InsertDocument(_store.Db, "eval-docs", fileName, title, hash, now, now);
        }
    }

    public void Dispose() => _store.Dispose();

    // =========================================================================
    // Helpers — match TS matchesExpected and calcHitRate
    // =========================================================================

    private static bool MatchesExpected(string filepath, string expectedDoc)
        => filepath.ToLowerInvariant().Contains(expectedDoc);

    private double CalcHitRate(IEnumerable<EvalQuery> queries, int topK)
    {
        var queryList = queries.ToList();
        int hits = 0;
        foreach (var eq in queryList)
        {
            var results = FtsSearcher.SearchFTS(_store.Db, eq.Query, 5);
            if (results.Take(topK).Any(r => MatchesExpected(r.Filepath, eq.ExpectedDoc)))
                hits++;
        }
        return (double)hits / queryList.Count;
    }

    // =========================================================================
    // Hit-rate tests — thresholds match eval-bm25.
    // =========================================================================

    [Fact]
    public void EasyQueries_HitRate_AtLeast80Percent_At3()
    {
        // TS: "easy queries: ≥80% Hit@3"
        var easyQueries = EvalQueries.Where(q => q.Difficulty == "easy");
        var hitRate = CalcHitRate(easyQueries, 3);
        hitRate.Should().BeGreaterThanOrEqualTo(0.8,
            $"easy queries should have ≥80% hit rate @3, got {hitRate:P0}");
    }

    [Fact]
    public void MediumQueries_HitRate_AtLeast15Percent_At3()
    {
        // TS: "medium queries: ≥15% Hit@3 (BM25 struggles with semantic)"
        var mediumQueries = EvalQueries.Where(q => q.Difficulty == "medium");
        var hitRate = CalcHitRate(mediumQueries, 3);
        hitRate.Should().BeGreaterThanOrEqualTo(0.15,
            $"medium queries should have ≥15% hit rate @3, got {hitRate:P0}");
    }

    [Fact]
    public void HardQueries_HitRate_AtLeast15Percent_At5()
    {
        // TS: "hard queries: ≥15% Hit@5 (BM25 baseline)"
        var hardQueries = EvalQueries.Where(q => q.Difficulty == "hard");
        var hitRate = CalcHitRate(hardQueries, 5);
        hitRate.Should().BeGreaterThanOrEqualTo(0.15,
            $"hard queries should have ≥15% hit rate @5, got {hitRate:P0}");
    }

    [Fact]
    public void OverallHitRate_AtLeast40Percent_At3()
    {
        // TS: "overall Hit@3 ≥40% (BM25 baseline)"
        var hitRate = CalcHitRate(EvalQueries, 3);
        hitRate.Should().BeGreaterThanOrEqualTo(0.4,
            $"overall hit rate should be ≥40% @3, got {hitRate:P0}");
    }
}
