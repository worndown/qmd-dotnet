using FluentAssertions;
using Qmd.Core.Configuration;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Search;
using Qmd.Core.Tests.Llm;

namespace Qmd.Core.Tests.Search;

public class VectorSearcherTests : IDisposable
{
    private readonly SqliteDatabase _db;

    public VectorSearcherTests()
    {
        _db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SearchVec_ReturnsEmpty_WhenNoVectorIndex()
    {
        // SearchVecAsync on DB without vectors_vec table returns empty
        var config = new CollectionConfig
        {
            Collections = new() { ["docs"] = new Collection { Path = "/docs" } }
        };
        ConfigSync.SyncToDb(_db, config);

        var hash = ContentHasher.HashContent("Some content");
        ContentHasher.InsertContent(_db, hash, "Some content", "2025-01-01");
        DocumentOperations.InsertDocument(_db, "docs", "doc1.md", "Doc 1", hash, "2025-01-01", "2025-01-01");

        var llm = new MockLlmService();
        // No vectors_vec table exists, should return empty
        var results = await VectorSearcher.SearchVecAsync(_db, "query", "embeddinggemma", llm, limit: 10);
        results.Should().BeEmpty();
    }
}
