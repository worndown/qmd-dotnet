using FluentAssertions;
using Qmd.Core.Configuration;
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
    private readonly FtsSearchService _fts;

    public FtsSearcherTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
        _fts = new FtsSearchService(_db);
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
        var results = _fts.Search("API");
        results.Should().NotBeEmpty();
        results[0].Title.Should().Be("API Reference");
    }

    [Fact]
    public void SearchFTS_ScoresNormalized()
    {
        var results = _fts.Search("API");
        foreach (var r in results)
        {
            r.Score.Should().BeGreaterThan(0);
            r.Score.Should().BeLessThan(1);
        }
    }

    [Fact]
    public void SearchFTS_OrderedByRelevance()
    {
        var results = _fts.Search("guide install configure");
        results.Should().NotBeEmpty();
        for (int i = 1; i < results.Count; i++)
            results[i].Score.Should().BeLessThanOrEqualTo(results[i - 1].Score);
    }

    [Fact]
    public void SearchFTS_RespectsLimit()
    {
        var results = _fts.Search("the", limit: 2);
        results.Should().HaveCountLessThanOrEqualTo(2);
    }

    [Fact]
    public void SearchFTS_FiltersByCollection()
    {
        var results = _fts.Search("systems", collections: ["notes"]);
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r => r.CollectionName.Should().Be("notes"));
    }

    [Fact]
    public void SearchFTS_ReturnsEmptyForNoMatch()
    {
        var results = _fts.Search("xyznonexistent");
        results.Should().BeEmpty();
    }

    [Fact]
    public void SearchFTS_EmptyQuery_ReturnsEmpty()
    {
        var results = _fts.Search("");
        results.Should().BeEmpty();
    }

    [Fact]
    public void SearchFTS_SetsDocid()
    {
        var results = _fts.Search("API");
        results[0].DocId.Should().HaveLength(6);
        results[0].DocId.Should().MatchRegex("^[a-f0-9]+$");
    }

    [Fact]
    public void SearchFTS_SetsSource()
    {
        var results = _fts.Search("API");
        results[0].Source.Should().Be("fts");
    }

    [Fact]
    public void SearchFTS_ExcludesInactiveDocuments()
    {
        new DocumentRepository(_db).DeactivateDocument("docs", "api.md");
        var results = _fts.Search("API endpoints");
        results.Should().NotContain(r => r.DisplayPath == "docs/api.md");
    }

    [Fact]
    public void SearchFTS_RanksTitleMatchesHigher()
    {
        using var db = TestDbHelper.CreateInMemoryDb();
        var fts = new FtsSearchService(db);

        var collectionName = "testcol";
        SeedCollectionInDb(db, collectionName, "/test");

        TestDbHelper.SeedDocument(db, collectionName, "test/body.md",
            "The fox is here in the body", "Some Other Title");

        TestDbHelper.SeedDocument(db, collectionName, "test/title.md",
            "Different content without the animal fox", "Fox Title");

        var results = fts.Search("fox");
        results.Should().HaveCount(2);
        results[0].DisplayPath.Should().Be($"{collectionName}/test/title.md");
    }

    [Fact]
    public void SearchFTS_HandlesSpecialCharactersInQuery()
    {
        using var db = TestDbHelper.CreateInMemoryDb();
        var fts = new FtsSearchService(db);

        var collectionName = "testcol";
        SeedCollectionInDb(db, collectionName, "/test");

        TestDbHelper.SeedDocument(db, collectionName, "test/doc1.md",
            "Function with params: foo(bar, baz)", "Functions");

        var results = fts.Search("foo(bar)");
        results.Should().BeOfType<List<SearchResult>>();
    }

    [Fact]
    public void SearchFTS_MinScoreFilter_KeepsStrongDropsWeak()
    {
        using var db = TestDbHelper.CreateInMemoryDb();
        var fts = new FtsSearchService(db);

        var collectionName = "testcol";
        SeedCollectionInDb(db, collectionName, "/test");

        for (int i = 0; i < 8; i++)
        {
            TestDbHelper.SeedDocument(db, collectionName, $"test/noise{i}.md",
                $"This document discusses completely different subjects like gardening and cooking {i}", $"Unrelated Topic {i}");
        }

        TestDbHelper.SeedDocument(db, collectionName, "test/strong.md",
            "Kubernetes deployment strategies for kubernetes clusters using kubernetes operators", "Kubernetes Deployment");

        TestDbHelper.SeedDocument(db, collectionName, "test/weak.md",
            "Various topics including a brief kubernetes mention among many other unrelated things", "Random Notes");

        var allResults = fts.Search("kubernetes");
        allResults.Should().HaveCount(2);

        var strongScore = allResults.First(r => r.DisplayPath.Contains("strong")).Score;
        var weakScore = allResults.First(r => r.DisplayPath.Contains("weak")).Score;

        var threshold = (strongScore + weakScore) / 2;
        var filtered = allResults.Where(r => r.Score >= threshold).ToList();

        filtered.Should().HaveCount(1);
        filtered[0].DisplayPath.Should().Contain("strong");
    }

    [Fact]
    public void SearchFTS_TitleBoostOutweighsHigherBodyFrequency()
    {
        using var db = TestDbHelper.CreateInMemoryDb();
        var fts = new FtsSearchService(db);

        var collectionName = "testcol";
        SeedCollectionInDb(db, collectionName, "/test");

        TestDbHelper.SeedDocument(db, collectionName, "test/body-only.md",
            "This research paper discusses quantum mechanics and the quantum model of computation. The quantum approach offers improvements over classical methods.", "General Science Notes");

        TestDbHelper.SeedDocument(db, collectionName, "test/title-match.md",
            "An introduction to the fundamentals of this emerging computing paradigm.", "Quantum Computing Overview");

        var results = fts.Search("quantum");
        results.Should().HaveCount(2);
        results[0].DisplayPath.Should().Be($"{collectionName}/test/title-match.md");
    }

    private static void SeedCollectionInDb(IQmdDatabase db, string name, string path)
    {
        var config = new CollectionConfig
        {
            Collections = new() { [name] = new Collection { Path = path } }
        };
        db.Prepare("DELETE FROM store_config WHERE key = $1").Run("config_hash");
        new ConfigSyncService(db).SyncToDb(config);
    }

    [Fact]
    public void SearchFTS_StrongSignalDetection_ScoreNormalizationCorrect()
    {
        using var db = TestDbHelper.CreateInMemoryDb();
        var fts = new FtsSearchService(db);

        var collectionName = "testcol";
        SeedCollectionInDb(db, collectionName, "/test");

        for (int i = 0; i < 50; i++)
        {
            TestDbHelper.SeedDocument(db, collectionName, $"test/noise{i}.md",
                $"Unrelated content about gardening, cooking, travel, music, and photography part {i}.", $"Noise Topic {i}");
        }

        TestDbHelper.SeedDocument(db, collectionName, "test/dominant.md",
            "Complete zephyr configuration guide. Zephyr setup instructions for zephyr deployment.", "Zephyr Configuration Guide");

        TestDbHelper.SeedDocument(db, collectionName, "test/weak.md",
            "Various topics covering many areas of technology and design. " +
            "One of them might relate to zephyr but mostly about other things entirely. " +
            "Additional content about databases, networking, security, performance, " +
            "monitoring, deployment, testing, and documentation practices.", "General Notes");

        var results = fts.Search("zephyr", limit: 10);
        results.Should().HaveCount(2);

        var topScore = results[0].Score;
        var secondScore = results[1].Score;

        topScore.Should().BeGreaterThanOrEqualTo(SearchConstants.StrongSignalMinScore);

        var gap = topScore - secondScore;
        gap.Should().BeGreaterThanOrEqualTo(SearchConstants.StrongSignalMinGap);

        var hasStrongSignal = topScore >= SearchConstants.StrongSignalMinScore
                              && gap >= SearchConstants.StrongSignalMinGap;
        hasStrongSignal.Should().BeTrue();
    }
}
