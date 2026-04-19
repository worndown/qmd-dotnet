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
    private readonly IQmdDatabase db;

    public VectorSearcherTests()
    {
        this.db = TestDbHelper.CreateInMemoryDb();
    }

    public void Dispose() => this.db.Dispose();

    [Fact]
    public async Task SearchVec_ReturnsEmpty_WhenNoVectorIndex()
    {
        var config = new CollectionConfig
        {
            Collections = new() { ["docs"] = new Collection { Path = "/docs" } }
        };
        new ConfigSyncService(this.db).SyncToDb(config);

        var docRepo = new DocumentRepository(this.db);
        var hash = ContentHasher.HashContent("Some content");
        docRepo.InsertContent(hash, "Some content", "2025-01-01");
        docRepo.InsertDocument("docs", "doc1.md", "Doc 1", hash, "2025-01-01", "2025-01-01");

        var llm = new MockLlmService();
        var vecService = new VectorSearchService(this.db, llm);
        var results = await vecService.SearchAsync("query", "embeddinggemma", limit: 10);
        results.Should().BeEmpty();
    }
}
