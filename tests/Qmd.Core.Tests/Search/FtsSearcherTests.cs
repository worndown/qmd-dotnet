using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Models;
using Qmd.Core.Search;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Search;

[Trait("Category", "Database")]
public class FtsSearcherTests : IDisposable
{
    private readonly IQmdDatabase _db;

    public FtsSearcherTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
        TestDbHelper.SeedDocuments(_db,
            ("docs", "api.md", "API Reference", "This document describes the REST API endpoints for authentication and authorization."),
            ("docs", "guide.md", "Getting Started", "Welcome to the getting started guide. Learn how to install and configure the system."),
            ("docs", "faq.md", "FAQ", "Frequently asked questions about performance, scaling, and deployment."),
            ("notes", "meeting.md", "Meeting Notes", "Discussion about multi-agent systems and their applications in distributed computing.")
        );
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void SearchFTS_ReturnsResults()
    {
        var results = FtsSearcher.SearchFTS(_db, "API");
        results.Should().NotBeEmpty();
        results[0].Title.Should().Be("API Reference");
    }

    [Fact]
    public void SearchFTS_ScoresNormalized()
    {
        var results = FtsSearcher.SearchFTS(_db, "API");
        foreach (var r in results)
        {
            r.Score.Should().BeGreaterThan(0);
            r.Score.Should().BeLessThan(1);
        }
    }

    [Fact]
    public void SearchFTS_OrderedByRelevance()
    {
        var results = FtsSearcher.SearchFTS(_db, "guide install configure");
        results.Should().NotBeEmpty();
        // Results should be ordered by score descending
        for (int i = 1; i < results.Count; i++)
            results[i].Score.Should().BeLessThanOrEqualTo(results[i - 1].Score);
    }

    [Fact]
    public void SearchFTS_RespectsLimit()
    {
        var results = FtsSearcher.SearchFTS(_db, "the", limit: 2);
        results.Should().HaveCountLessThanOrEqualTo(2);
    }

    [Fact]
    public void SearchFTS_FiltersByCollection()
    {
        var results = FtsSearcher.SearchFTS(_db, "systems", collections: ["notes"]);
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r => r.CollectionName.Should().Be("notes"));
    }

    [Fact]
    public void SearchFTS_ReturnsEmptyForNoMatch()
    {
        var results = FtsSearcher.SearchFTS(_db, "xyznonexistent");
        results.Should().BeEmpty();
    }

    [Fact]
    public void SearchFTS_EmptyQuery_ReturnsEmpty()
    {
        var results = FtsSearcher.SearchFTS(_db, "");
        results.Should().BeEmpty();
    }

    [Fact]
    public void SearchFTS_SetsDocid()
    {
        var results = FtsSearcher.SearchFTS(_db, "API");
        results[0].DocId.Should().HaveLength(6);
        results[0].DocId.Should().MatchRegex("^[a-f0-9]+$");
    }

    [Fact]
    public void SearchFTS_SetsSource()
    {
        var results = FtsSearcher.SearchFTS(_db, "API");
        results[0].Source.Should().Be("fts");
    }

    [Fact]
    public void SearchFTS_ExcludesInactiveDocuments()
    {
        DocumentOperations.DeactivateDocument(_db, "docs", "api.md");
        var results = FtsSearcher.SearchFTS(_db, "API endpoints");
        results.Should().NotContain(r => r.DisplayPath == "docs/api.md");
    }

    [Fact]
    public void SearchFTS_RanksTitleMatchesHigher()
    {
        // Create a fresh DB so seeded docs don't interfere
        using var db = TestDbHelper.CreateInMemoryDb();

        var collectionName = "testcol";
        SeedCollectionInDb(db, collectionName, "/test");

        // Document with "fox" in body only
        TestDbHelper.SeedDocument(db, collectionName, "test/body.md",
            "The fox is here in the body", "Some Other Title");

        // Document with "fox" in title — title column has 4x BM25 weight
        TestDbHelper.SeedDocument(db, collectionName, "test/title.md",
            "Different content without the animal fox", "Fox Title");

        var results = FtsSearcher.SearchFTS(db, "fox");
        results.Should().HaveCount(2);
        // Title/name match should rank higher due to BM25 weights
        results[0].DisplayPath.Should().Be($"{collectionName}/test/title.md");
    }

    [Fact]
    public void SearchFTS_HandlesSpecialCharactersInQuery()
    {
        using var db = TestDbHelper.CreateInMemoryDb();

        var collectionName = "testcol";
        SeedCollectionInDb(db, collectionName, "/test");

        TestDbHelper.SeedDocument(db, collectionName, "test/doc1.md",
            "Function with params: foo(bar, baz)", "Functions");

        // Should not throw on special characters
        var results = FtsSearcher.SearchFTS(db, "foo(bar)");
        results.Should().BeOfType<List<Models.SearchResult>>();
    }

    [Fact]
    public void SearchFTS_MinScoreFilter_KeepsStrongDropsWeak()
    {
        using var db = TestDbHelper.CreateInMemoryDb();

        var collectionName = "testcol";
        SeedCollectionInDb(db, collectionName, "/test");

        // Add noise documents for meaningful IDF
        for (int i = 0; i < 8; i++)
        {
            TestDbHelper.SeedDocument(db, collectionName, $"test/noise{i}.md",
                $"This document discusses completely different subjects like gardening and cooking {i}", $"Unrelated Topic {i}");
        }

        // Strong match: keyword in title (4x weight) + repeated in body
        TestDbHelper.SeedDocument(db, collectionName, "test/strong.md",
            "Kubernetes deployment strategies for kubernetes clusters using kubernetes operators", "Kubernetes Deployment");

        // Weak match: keyword appears once in body only
        TestDbHelper.SeedDocument(db, collectionName, "test/weak.md",
            "Various topics including a brief kubernetes mention among many other unrelated things", "Random Notes");

        var allResults = FtsSearcher.SearchFTS(db, "kubernetes");
        allResults.Should().HaveCount(2);

        var strongScore = allResults.First(r => r.DisplayPath.Contains("strong")).Score;
        var weakScore = allResults.First(r => r.DisplayPath.Contains("weak")).Score;

        // Find a threshold between them
        var threshold = (strongScore + weakScore) / 2;
        var filtered = allResults.Where(r => r.Score >= threshold).ToList();

        // Strong match survives the filter, weak does not
        filtered.Should().HaveCount(1);
        filtered[0].DisplayPath.Should().Contain("strong");
    }

    [Fact]
    public void SearchFTS_TitleBoostOutweighsHigherBodyFrequency()
    {
        // Title boost outweighs higher body frequency
        // Document with term in title but not body ranks higher than one with term in body 5x
        using var db = TestDbHelper.CreateInMemoryDb();

        var collectionName = "testcol";
        SeedCollectionInDb(db, collectionName, "/test");

        // Document with "quantum" mentioned in a longer body but NOT in the title
        TestDbHelper.SeedDocument(db, collectionName, "test/body-only.md",
            "This research paper discusses quantum mechanics and the quantum model of computation. The quantum approach offers improvements over classical methods.", "General Science Notes");

        // Document with "quantum" in the title but a shorter body mention
        TestDbHelper.SeedDocument(db, collectionName, "test/title-match.md",
            "An introduction to the fundamentals of this emerging computing paradigm.", "Quantum Computing Overview");

        var results = FtsSearcher.SearchFTS(db, "quantum");
        results.Should().HaveCount(2);
        // Title-match doc should rank higher due to BM25 column weights boosting title
        results[0].DisplayPath.Should().Be($"{collectionName}/test/title-match.md");
    }

    private static void SeedCollectionInDb(IQmdDatabase db, string name, string path)
    {
        var config = new Qmd.Core.Configuration.CollectionConfig
        {
            Collections = new() { [name] = new Qmd.Core.Configuration.Collection { Path = path } }
        };
        db.Prepare("DELETE FROM store_config WHERE key = $1").Run("config_hash");
        Qmd.Core.Configuration.ConfigSync.SyncToDb(db, config);
    }

    [Fact]
    public void SearchFTS_StrongSignalDetection_ScoreNormalizationCorrect()
    {
        // BM25 strong signal detection works with correct score normalization
        // BM25 IDF needs meaningful corpus depth for strong signal to fire.
        using var db = TestDbHelper.CreateInMemoryDb();

        var collectionName = "testcol";
        SeedCollectionInDb(db, collectionName, "/test");

        // 50 noise docs give IDF enough for scores above 0.85
        for (int i = 0; i < 50; i++)
        {
            TestDbHelper.SeedDocument(db, collectionName, $"test/noise{i}.md",
                $"Unrelated content about gardening, cooking, travel, music, and photography part {i}.", $"Noise Topic {i}");
        }

        // Dominant: keyword in title + body (multiple occurrences)
        TestDbHelper.SeedDocument(db, collectionName, "test/dominant.md",
            "Complete zephyr configuration guide. Zephyr setup instructions for zephyr deployment.", "Zephyr Configuration Guide");

        // Weak: keyword once in body only, longer doc dilutes TF
        TestDbHelper.SeedDocument(db, collectionName, "test/weak.md",
            "Various topics covering many areas of technology and design. " +
            "One of them might relate to zephyr but mostly about other things entirely. " +
            "Additional content about databases, networking, security, performance, " +
            "monitoring, deployment, testing, and documentation practices.", "General Notes");

        var results = FtsSearcher.SearchFTS(db, "zephyr", limit: 10);
        results.Should().HaveCount(2);

        var topScore = results[0].Score;
        var secondScore = results[1].Score;

        // With correct normalization: strong match should be well above threshold
        topScore.Should().BeGreaterThanOrEqualTo(SearchConstants.StrongSignalMinScore);

        // Gap should exceed threshold when there's a dominant match
        var gap = topScore - secondScore;
        gap.Should().BeGreaterThanOrEqualTo(SearchConstants.StrongSignalMinGap);

        // Full strong signal check should pass
        var hasStrongSignal = topScore >= SearchConstants.StrongSignalMinScore
                              && gap >= SearchConstants.StrongSignalMinGap;
        hasStrongSignal.Should().BeTrue();
    }
}
