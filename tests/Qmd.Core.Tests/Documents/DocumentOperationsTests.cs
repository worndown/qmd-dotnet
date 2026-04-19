using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Documents;

[Trait("Category", "Database")]
public class DocumentOperationsTests : IDisposable
{
    private readonly IQmdDatabase db;
    private readonly DocumentRepository repo;

    public DocumentOperationsTests()
    {
        this.db = TestDbHelper.CreateInMemoryDb();
        this.repo = new DocumentRepository(this.db);
        // Seed content for FK
        this.repo.InsertContent("hash1", "Content 1", "2025-01-01");
        this.repo.InsertContent("hash2", "Content 2", "2025-01-01");
    }

    public void Dispose() => this.db.Dispose();

    [Fact]
    public void InsertDocument_CreatesNewDocument()
    {
        this.repo.InsertDocument("docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        var row = this.db.Prepare("SELECT title FROM documents WHERE collection = $1 AND path = $2")
            .Get<TitleRow>("docs", "readme.md");
        row.Should().NotBeNull();
        row!.Title.Should().Be("README");
    }

    [Fact]
    public void InsertDocument_UpsertsOnConflict()
    {
        this.repo.InsertDocument("docs", "readme.md", "Old Title", "hash1", "2025-01-01", "2025-01-01");
        this.repo.InsertDocument("docs", "readme.md", "New Title", "hash2", "2025-01-01", "2025-01-02");

        var row = this.db.Prepare("SELECT title, hash FROM documents WHERE collection = $1 AND path = $2")
            .Get<TitleHashRow>("docs", "readme.md");
        row!.Title.Should().Be("New Title");
        row.Hash.Should().Be("hash2");
    }

    [Fact]
    public void FindActiveDocument_ReturnsRow()
    {
        this.repo.InsertDocument("docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        var result = this.repo.FindActiveDocument("docs", "readme.md");
        result.Should().NotBeNull();
        result!.Hash.Should().Be("hash1");
        result.Title.Should().Be("README");
    }

    [Fact]
    public void FindActiveDocument_ReturnsNull_WhenNotFound()
    {
        this.repo.FindActiveDocument("docs", "nonexistent.md").Should().BeNull();
    }

    [Fact]
    public void UpdateDocumentTitle_ChangesTitle()
    {
        this.repo.InsertDocument("docs", "readme.md", "Old", "hash1", "2025-01-01", "2025-01-01");
        var doc = this.repo.FindActiveDocument("docs", "readme.md")!;
        this.repo.UpdateDocumentTitle(doc.Id, "New Title", "2025-01-02");

        var updated = this.repo.FindActiveDocument("docs", "readme.md")!;
        updated.Title.Should().Be("New Title");
    }

    [Fact]
    public void UpdateDocument_ChangesHashAndTitle()
    {
        this.repo.InsertDocument("docs", "readme.md", "Old", "hash1", "2025-01-01", "2025-01-01");
        var doc = this.repo.FindActiveDocument("docs", "readme.md")!;
        this.repo.UpdateDocument(doc.Id, "Updated", "hash2", "2025-01-02");

        var updated = this.repo.FindActiveDocument("docs", "readme.md")!;
        updated.Title.Should().Be("Updated");
        updated.Hash.Should().Be("hash2");
    }

    [Fact]
    public void DeactivateDocument_SetsInactive()
    {
        this.repo.InsertDocument("docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        this.repo.DeactivateDocument("docs", "readme.md");
        this.repo.FindActiveDocument("docs", "readme.md").Should().BeNull();
    }

    [Fact]
    public void GetActiveDocumentPaths_ReturnsActivePaths()
    {
        this.repo.InsertDocument("docs", "a.md", "A", "hash1", "2025-01-01", "2025-01-01");
        this.repo.InsertDocument("docs", "b.md", "B", "hash2", "2025-01-01", "2025-01-01");
        this.repo.DeactivateDocument("docs", "b.md");

        var paths = this.repo.GetActiveDocumentPaths("docs");
        paths.Should().Contain("a.md");
        paths.Should().NotContain("b.md");
    }

    [Fact]
    public void FtsTrigger_SyncsOnInsert()
    {
        this.repo.InsertDocument("docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        var fts = this.db.Prepare("SELECT filepath FROM documents_fts WHERE documents_fts MATCH $1")
            .Get<FilepathRow>("Content");
        fts.Should().NotBeNull();
        fts!.Filepath.Should().Be("docs/readme.md");
    }

    [Fact]
    public void FtsTrigger_RemovesOnDeactivate()
    {
        this.repo.InsertDocument("docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");
        this.repo.DeactivateDocument("docs", "readme.md");
        var fts = this.db.Prepare("SELECT filepath FROM documents_fts WHERE documents_fts MATCH $1")
            .Get<FilepathRow>("Content");
        fts.Should().BeNull();
    }

    private class TitleRow
    {
        public string Title { get; set; } = "";
    }

    private class TitleHashRow
    {
        public string Title { get; set; } = "";
        public string Hash { get; set; } = "";
    }

    private class FilepathRow
    {
        public string Filepath { get; set; } = "";
    }
}
