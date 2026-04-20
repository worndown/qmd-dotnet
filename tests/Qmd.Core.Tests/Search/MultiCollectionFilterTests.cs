using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Search;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Search;

[Trait("Category", "Database")]
public class MultiCollectionFilterTests : IDisposable
{
    private readonly IQmdDatabase db;

    public MultiCollectionFilterTests()
    {
        this.db = TestDbHelper.CreateInMemoryDb();
        TestDbHelper.SeedDocuments(this.db,
            ("docs", "api.md", "API Reference", "REST API endpoints for authentication."),
            ("docs", "guide.md", "Getting Started", "How to install and configure the system."),
            ("code", "auth.ts", "Auth Module", "OAuth2 authentication flow implementation."),
            ("notes", "meeting.md", "Meeting Notes", "Discussion about API design patterns.")
        );
    }

    public void Dispose() => this.db.Dispose();

    [Fact]
    public void SearchFTS_NoCollectionFilter_ReturnsAll()
    {
        var results = new FtsSearchService(this.db).Search( "authentication");
        results.Should().HaveCountGreaterThanOrEqualTo(2);
        var collections = results.Select(r => r.CollectionName).Distinct().ToList();
        collections.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void SearchFTS_SingleCollection_FiltersCorrectly()
    {
        var results = new FtsSearchService(this.db).Search( "authentication", collections: ["docs"]);
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r => r.CollectionName.Should().Be("docs"));
    }

    [Fact]
    public void SearchFTS_MultipleCollections_OrMatch()
    {
        var results = new FtsSearchService(this.db).Search( "authentication", collections: ["docs", "code"]);
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r =>
            new[] { "docs", "code" }.Should().Contain(r.CollectionName));
    }

    [Fact]
    public void SearchFTS_MultipleCollections_ExcludesOthers()
    {
        var results = new FtsSearchService(this.db).Search( "API", collections: ["docs", "notes"]);
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r =>
            r.CollectionName.Should().NotBe("code"));
    }

    [Fact]
    public void SearchFTS_NonexistentCollection_ReturnsEmpty()
    {
        var results = new FtsSearchService(this.db).Search( "API", collections: ["nonexistent"]);
        results.Should().BeEmpty();
    }

    [Fact]
    public void SearchFTS_EmptyCollectionList_SearchesAll()
    {
        // null or empty list should search all collections
        var results = new FtsSearchService(this.db).Search( "API", collections: null);
        results.Should().NotBeEmpty();
        var resultsEmpty = new FtsSearchService(this.db).Search( "API", collections: []);
        resultsEmpty.Should().NotBeEmpty();
        resultsEmpty.Should().HaveCount(results.Count);
    }

    [Fact]
    public void SearchFTS_SingleCollectionAsList_MatchesCollectionFilter()
    {
        var withList = new FtsSearchService(this.db).Search( "authentication", collections: ["docs"]);
        // Should only get docs results
        withList.Should().NotBeEmpty();
        withList.Should().AllSatisfy(r => r.CollectionName.Should().Be("docs"));
    }
}
