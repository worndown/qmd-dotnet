using FluentAssertions;
using Qmd.Core.Content;
using Qmd.Core.Configuration;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Indexing;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Indexing;

[Trait("Category", "Database")]
public class StatusOperationsTests : IDisposable
{
    private readonly IQmdDatabase _db;

    public StatusOperationsTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
    }

    public void Dispose() => _db.Dispose();

    private void SeedCollection(string name, string path)
    {
        var config = new CollectionConfig
        {
            Collections = new() { [name] = new Collection { Path = path } }
        };
        ConfigSync.SyncToDb(_db, config);
    }

    private void SeedDoc(string collection, string docPath, string content)
    {
        var hash = ContentHasher.HashContent(content);
        ContentHasher.InsertContent(_db, hash, content, "2025-01-01");
        DocumentOperations.InsertDocument(_db, collection, docPath, "Title", hash, "2025-01-01", "2025-01-01");
    }

    [Fact]
    public void GetStatus_ReturnsCorrectCounts()
    {
        SeedCollection("docs", "/docs");
        SeedDoc("docs", "a.md", "Content A");
        SeedDoc("docs", "b.md", "Content B");

        var status = StatusOperations.GetStatus(_db);
        status.TotalDocuments.Should().Be(2);
        status.Collections.Should().HaveCount(1);
        status.Collections[0].Name.Should().Be("docs");
        status.Collections[0].Documents.Should().Be(2);
    }

    [Fact]
    public void GetHashesNeedingEmbedding_CountsCorrectly()
    {
        SeedCollection("docs", "/docs");
        SeedDoc("docs", "a.md", "Content A");

        // No embeddings yet, all should need embedding
        StatusOperations.GetHashesNeedingEmbedding(_db).Should().Be(1);
    }

    [Fact]
    public void GetIndexHealth_ReturnsInfo()
    {
        SeedCollection("docs", "/docs");
        SeedDoc("docs", "a.md", "Content A");

        var health = StatusOperations.GetIndexHealth(_db);
        health.TotalDocs.Should().Be(1);
        health.NeedsEmbedding.Should().Be(1);
    }

    [Fact]
    public void GetStatus_ReportsCollectionInfo()
    {
        // Verify collections list includes name, path, doc_count
        SeedCollection("testcol", "/test/path");

        // Seed with explicit pattern
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["testcol"] = new Collection { Path = "/test/path", Pattern = "**/*.md" }
            }
        };
        _db.Prepare("DELETE FROM store_config WHERE key = $1").Run("config_hash");
        ConfigSync.SyncToDb(_db, config);

        SeedDoc("testcol", "doc1.md", "First doc content");

        var status = StatusOperations.GetStatus(_db);
        status.Collections.Should().HaveCountGreaterThanOrEqualTo(1);
        var col = status.Collections.FirstOrDefault(c => c.Name == "testcol");
        col.Should().NotBeNull();
        col!.Path.Should().Be("/test/path");
        col.Pattern.Should().Be("**/*.md");
        col.Documents.Should().Be(1);
    }
}
