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
    private readonly IQmdDatabase db;
    private readonly StatusRepository statusRepo;
    private readonly DocumentRepository docRepo;
    private readonly ConfigSyncService configSync;

    public StatusOperationsTests()
    {
        this.db = TestDbHelper.CreateInMemoryDb();
        this.statusRepo = new StatusRepository(this.db);
        this.docRepo = new DocumentRepository(this.db);
        this.configSync = new ConfigSyncService(this.db);
    }

    public void Dispose() => this.db.Dispose();

    private void SeedCollection(string name, string path)
    {
        var config = new CollectionConfig
        {
            Collections = new() { [name] = new Collection { Path = path } }
        };
        this.configSync.SyncToDb(config);
    }

    private void SeedDoc(string collection, string docPath, string content)
    {
        var hash = ContentHasher.HashContent(content);
        this.docRepo.InsertContent(hash, content, "2025-01-01");
        this.docRepo.InsertDocument(collection, docPath, "Title", hash, "2025-01-01", "2025-01-01");
    }

    [Fact]
    public void GetStatus_ReturnsCorrectCounts()
    {
        this.SeedCollection("docs", "/docs");
        this.SeedDoc("docs", "a.md", "Content A");
        this.SeedDoc("docs", "b.md", "Content B");

        var status = this.statusRepo.GetStatus();
        status.TotalDocuments.Should().Be(2);
        status.Collections.Should().HaveCount(1);
        status.Collections[0].Name.Should().Be("docs");
        status.Collections[0].Documents.Should().Be(2);
    }

    [Fact]
    public void GetHashesNeedingEmbedding_CountsCorrectly()
    {
        this.SeedCollection("docs", "/docs");
        this.SeedDoc("docs", "a.md", "Content A");

        // No embeddings yet, all should need embedding
        this.statusRepo.GetHashesNeedingEmbedding().Should().Be(1);
    }

    [Fact]
    public void GetIndexHealth_ReturnsInfo()
    {
        this.SeedCollection("docs", "/docs");
        this.SeedDoc("docs", "a.md", "Content A");

        var health = this.statusRepo.GetIndexHealth();
        health.TotalDocs.Should().Be(1);
        health.NeedsEmbedding.Should().Be(1);
    }

    [Fact]
    public void GetStatus_ReportsCollectionInfo()
    {
        // Verify collections list includes name, path, doc_count
        this.SeedCollection("testcol", "/test/path");

        // Seed with explicit pattern
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["testcol"] = new Collection { Path = "/test/path", Pattern = "**/*.md" }
            }
        };
        this.db.Prepare("DELETE FROM store_config WHERE key = $1").Run("config_hash");
        this.configSync.SyncToDb(config);

        this.SeedDoc("testcol", "doc1.md", "First doc content");

        var status = this.statusRepo.GetStatus();
        status.Collections.Should().HaveCountGreaterThanOrEqualTo(1);
        var col = status.Collections.FirstOrDefault(c => c.Name == "testcol");
        col.Should().NotBeNull();
        col!.Path.Should().Be("/test/path");
        col.Pattern.Should().Be("**/*.md");
        col.Documents.Should().Be(1);
    }
}
