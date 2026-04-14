using FluentAssertions;
using Qmd.Core.Configuration;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Search;
using Qmd.Core.Tests.Llm;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Search;

[Trait("Category", "Database")]
public class VectorSearcherTests : IDisposable
{
    private readonly IQmdDatabase _db;

    public VectorSearcherTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SearchVec_ReturnsEmpty_WhenNoVectorIndex()
    {
        var config = new CollectionConfig
        {
            Collections = new() { ["docs"] = new Collection { Path = "/docs" } }
        };
        new ConfigSyncService(_db).SyncToDb(config);

        var docRepo = new DocumentRepository(_db);
        var hash = ContentHasher.HashContent("Some content");
        docRepo.InsertContent(hash, "Some content", "2025-01-01");
        docRepo.InsertDocument("docs", "doc1.md", "Doc 1", hash, "2025-01-01", "2025-01-01");

        var llm = new MockLlmService();
        var vecService = new VectorSearchService(_db, llm);
        var results = await vecService.SearchAsync("query", "embeddinggemma", limit: 10);
        results.Should().BeEmpty();
    }
}
