using FluentAssertions;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Search;

namespace Qmd.Core.Tests.Search;

public class MultiCollectionFilterTests : IDisposable
{
    private readonly SqliteDatabase _db;

    public MultiCollectionFilterTests()
    {
        _db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(_db);
        SeedDocuments();
    }

    public void Dispose() => _db.Dispose();

    private void SeedDocuments()
    {
        void Seed(string collection, string path, string title, string content)
        {
            var hash = ContentHasher.HashContent(content);
            ContentHasher.InsertContent(_db, hash, content, "2025-01-01");
            DocumentOperations.InsertDocument(_db, collection, path, title, hash, "2025-01-01", "2025-01-01");
        }

        Seed("docs", "api.md", "API Reference", "REST API endpoints for authentication.");
        Seed("docs", "guide.md", "Getting Started", "How to install and configure the system.");
        Seed("code", "auth.ts", "Auth Module", "OAuth2 authentication flow implementation.");
        Seed("notes", "meeting.md", "Meeting Notes", "Discussion about API design patterns.");
    }

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
