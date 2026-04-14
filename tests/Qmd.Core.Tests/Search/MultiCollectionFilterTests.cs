using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Search;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Search;

[Trait("Category", "Database")]
public class MultiCollectionFilterTests : IDisposable
{
    private readonly IQmdDatabase _db;

    public MultiCollectionFilterTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
        TestDbHelper.SeedDocuments(_db,
            ("docs", "api.md", "API Reference", "REST API endpoints for authentication."),
            ("docs", "guide.md", "Getting Started", "How to install and configure the system."),
            ("code", "auth.ts", "Auth Module", "OAuth2 authentication flow implementation."),
            ("notes", "meeting.md", "Meeting Notes", "Discussion about API design patterns.")
        );
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void SearchFTS_NoCollectionFilter_ReturnsAll()
    {
        var results = FtsSearcher.SearchFTS(_db, "authentication");
        results.Should().HaveCountGreaterThanOrEqualTo(2);
        var collections = results.Select(r => r.CollectionName).Distinct().ToList();
        collections.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void SearchFTS_SingleCollection_FiltersCorrectly()
    {
        var results = FtsSearcher.SearchFTS(_db, "authentication", collections: ["docs"]);
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r => r.CollectionName.Should().Be("docs"));
    }

    [Fact]
    public void SearchFTS_MultipleCollections_OrMatch()
    {
        var results = FtsSearcher.SearchFTS(_db, "authentication", collections: ["docs", "code"]);
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r =>
            new[] { "docs", "code" }.Should().Contain(r.CollectionName));
    }

    [Fact]
    public void SearchFTS_MultipleCollections_ExcludesOthers()
    {
        var results = FtsSearcher.SearchFTS(_db, "API", collections: ["docs", "notes"]);
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r =>
            r.CollectionName.Should().NotBe("code"));
    }

    [Fact]
    public void SearchFTS_NonexistentCollection_ReturnsEmpty()
    {
        var results = FtsSearcher.SearchFTS(_db, "API", collections: ["nonexistent"]);
        results.Should().BeEmpty();
    }

    [Fact]
    public void SearchFTS_EmptyCollectionList_SearchesAll()
    {
        // null or empty list should search all collections
        var results = FtsSearcher.SearchFTS(_db, "API", collections: null);
        results.Should().NotBeEmpty();
        var resultsEmpty = FtsSearcher.SearchFTS(_db, "API", collections: []);
        resultsEmpty.Should().NotBeEmpty();
        resultsEmpty.Should().HaveCount(results.Count);
    }

    [Fact]
    public void SearchFTS_SingleCollectionAsList_MatchesCollectionFilter()
    {
        var withList = FtsSearcher.SearchFTS(_db, "authentication", collections: ["docs"]);
        // Should only get docs results
        withList.Should().NotBeEmpty();
        withList.Should().AllSatisfy(r => r.CollectionName.Should().Be("docs"));
    }
}
