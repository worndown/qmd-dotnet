using FluentAssertions;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Documents;

[Trait("Category", "Database")]
public class DocumentOperationsTests : IDisposable
{
    private readonly IQmdDatabase _db;

    public DocumentOperationsTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
        // Seed content for FK
        ContentHasher.InsertContent(_db, "hash1", "Content 1", "2025-01-01");
        ContentHasher.InsertContent(_db, "hash2", "Content 2", "2025-01-01");
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void InsertDocument_CreatesNewDocument()
    {
        DocumentOperations.InsertDocument(_db, "docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        var row = _db.Prepare("SELECT title FROM documents WHERE collection = $1 AND path = $2")
            .GetDynamic("docs", "readme.md");
        row.Should().NotBeNull();
        row!["title"].Should().Be("README");
    }

    [Fact]
    public void InsertDocument_UpsertsOnConflict()
    {
        DocumentOperations.InsertDocument(_db, "docs", "readme.md", "Old Title", "hash1", "2025-01-01", "2025-01-01");
        DocumentOperations.InsertDocument(_db, "docs", "readme.md", "New Title", "hash2", "2025-01-01", "2025-01-02");

        var row = _db.Prepare("SELECT title, hash FROM documents WHERE collection = $1 AND path = $2")
            .GetDynamic("docs", "readme.md");
        row!["title"].Should().Be("New Title");
        row["hash"].Should().Be("hash2");
    }

    [Fact]
    public void FindActiveDocument_ReturnsRow()
    {
        DocumentOperations.InsertDocument(_db, "docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        var result = DocumentOperations.FindActiveDocument(_db, "docs", "readme.md");
        result.Should().NotBeNull();
        result!.Hash.Should().Be("hash1");
        result.Title.Should().Be("README");
    }

    [Fact]
    public void FindActiveDocument_ReturnsNull_WhenNotFound()
    {
        DocumentOperations.FindActiveDocument(_db, "docs", "nonexistent.md").Should().BeNull();
    }

    [Fact]
    public void UpdateDocumentTitle_ChangesTitle()
    {
        DocumentOperations.InsertDocument(_db, "docs", "readme.md", "Old", "hash1", "2025-01-01", "2025-01-01");
        var doc = DocumentOperations.FindActiveDocument(_db, "docs", "readme.md")!;
        DocumentOperations.UpdateDocumentTitle(_db, doc.Id, "New Title", "2025-01-02");

        var updated = DocumentOperations.FindActiveDocument(_db, "docs", "readme.md")!;
        updated.Title.Should().Be("New Title");
    }

    [Fact]
    public void UpdateDocument_ChangesHashAndTitle()
    {
        DocumentOperations.InsertDocument(_db, "docs", "readme.md", "Old", "hash1", "2025-01-01", "2025-01-01");
        var doc = DocumentOperations.FindActiveDocument(_db, "docs", "readme.md")!;
        DocumentOperations.UpdateDocument(_db, doc.Id, "Updated", "hash2", "2025-01-02");

        var updated = DocumentOperations.FindActiveDocument(_db, "docs", "readme.md")!;
        updated.Title.Should().Be("Updated");
        updated.Hash.Should().Be("hash2");
    }

    [Fact]
    public void DeactivateDocument_SetsInactive()
    {
        DocumentOperations.InsertDocument(_db, "docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        DocumentOperations.DeactivateDocument(_db, "docs", "readme.md");
        DocumentOperations.FindActiveDocument(_db, "docs", "readme.md").Should().BeNull();
    }

    [Fact]
    public void GetActiveDocumentPaths_ReturnsActivePaths()
    {
        DocumentOperations.InsertDocument(_db, "docs", "a.md", "A", "hash1", "2025-01-01", "2025-01-01");
        DocumentOperations.InsertDocument(_db, "docs", "b.md", "B", "hash2", "2025-01-01", "2025-01-01");
        DocumentOperations.DeactivateDocument(_db, "docs", "b.md");

        var paths = DocumentOperations.GetActiveDocumentPaths(_db, "docs");
        paths.Should().Contain("a.md");
        paths.Should().NotContain("b.md");
    }

    [Fact]
    public void FtsTrigger_SyncsOnInsert()
    {
        DocumentOperations.InsertDocument(_db, "docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        var fts = _db.Prepare("SELECT filepath FROM documents_fts WHERE documents_fts MATCH $1")
            .GetDynamic("Content");
        fts.Should().NotBeNull();
        fts!["filepath"].Should().Be("docs/readme.md");
    }

    [Fact]
    public void FtsTrigger_RemovesOnDeactivate()
    {
        DocumentOperations.InsertDocument(_db, "docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        DocumentOperations.DeactivateDocument(_db, "docs", "readme.md");
        var fts = _db.Prepare("SELECT filepath FROM documents_fts WHERE documents_fts MATCH $1")
            .GetDynamic("Content");
        fts.Should().BeNull();
    }
}
