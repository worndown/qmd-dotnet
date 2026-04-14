using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Documents;

[Trait("Category", "Database")]
public class DocumentOperationsTests : IDisposable
{
    private readonly IQmdDatabase _db;
    private readonly DocumentRepository _repo;

    public DocumentOperationsTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
        _repo = new DocumentRepository(_db);
        // Seed content for FK
        _repo.InsertContent("hash1", "Content 1", "2025-01-01");
        _repo.InsertContent("hash2", "Content 2", "2025-01-01");
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void InsertDocument_CreatesNewDocument()
    {
        _repo.InsertDocument("docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        var row = _db.Prepare("SELECT title FROM documents WHERE collection = $1 AND path = $2")
            .GetDynamic("docs", "readme.md");
        row.Should().NotBeNull();
        row!["title"].Should().Be("README");
    }

    [Fact]
    public void InsertDocument_UpsertsOnConflict()
    {
        _repo.InsertDocument("docs", "readme.md", "Old Title", "hash1", "2025-01-01", "2025-01-01");
        _repo.InsertDocument("docs", "readme.md", "New Title", "hash2", "2025-01-01", "2025-01-02");

        var row = _db.Prepare("SELECT title, hash FROM documents WHERE collection = $1 AND path = $2")
            .GetDynamic("docs", "readme.md");
        row!["title"].Should().Be("New Title");
        row["hash"].Should().Be("hash2");
    }

    [Fact]
    public void FindActiveDocument_ReturnsRow()
    {
        _repo.InsertDocument("docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        var result = _repo.FindActiveDocument("docs", "readme.md");
        result.Should().NotBeNull();
        result!.Hash.Should().Be("hash1");
        result.Title.Should().Be("README");
    }

    [Fact]
    public void FindActiveDocument_ReturnsNull_WhenNotFound()
    {
        _repo.FindActiveDocument("docs", "nonexistent.md").Should().BeNull();
    }

    [Fact]
    public void UpdateDocumentTitle_ChangesTitle()
    {
        _repo.InsertDocument("docs", "readme.md", "Old", "hash1", "2025-01-01", "2025-01-01");
        var doc = _repo.FindActiveDocument("docs", "readme.md")!;
        _repo.UpdateDocumentTitle(doc.Id, "New Title", "2025-01-02");

        var updated = _repo.FindActiveDocument("docs", "readme.md")!;
        updated.Title.Should().Be("New Title");
    }

    [Fact]
    public void UpdateDocument_ChangesHashAndTitle()
    {
        _repo.InsertDocument("docs", "readme.md", "Old", "hash1", "2025-01-01", "2025-01-01");
        var doc = _repo.FindActiveDocument("docs", "readme.md")!;
        _repo.UpdateDocument(doc.Id, "Updated", "hash2", "2025-01-02");

        var updated = _repo.FindActiveDocument("docs", "readme.md")!;
        updated.Title.Should().Be("Updated");
        updated.Hash.Should().Be("hash2");
    }

    [Fact]
    public void DeactivateDocument_SetsInactive()
    {
        _repo.InsertDocument("docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        _repo.DeactivateDocument("docs", "readme.md");
        _repo.FindActiveDocument("docs", "readme.md").Should().BeNull();
    }

    [Fact]
    public void GetActiveDocumentPaths_ReturnsActivePaths()
    {
        _repo.InsertDocument("docs", "a.md", "A", "hash1", "2025-01-01", "2025-01-01");
        _repo.InsertDocument("docs", "b.md", "B", "hash2", "2025-01-01", "2025-01-01");
        _repo.DeactivateDocument("docs", "b.md");

        var paths = _repo.GetActiveDocumentPaths("docs");
        paths.Should().Contain("a.md");
        paths.Should().NotContain("b.md");
    }

    [Fact]
    public void FtsTrigger_SyncsOnInsert()
    {
        _repo.InsertDocument("docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        var fts = _db.Prepare("SELECT filepath FROM documents_fts WHERE documents_fts MATCH $1")
            .GetDynamic("Content");
        fts.Should().NotBeNull();
        fts!["filepath"].Should().Be("docs/readme.md");
    }

    [Fact]
    public void FtsTrigger_RemovesOnDeactivate()
    {
        _repo.InsertDocument("docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        _repo.DeactivateDocument("docs", "readme.md");
        var fts = _db.Prepare("SELECT filepath FROM documents_fts WHERE documents_fts MATCH $1")
            .GetDynamic("Content");
        fts.Should().BeNull();
    }
}
