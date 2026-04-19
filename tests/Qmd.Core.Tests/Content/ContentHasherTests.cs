using FluentAssertions;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Content;

[Trait("Category", "Database")]
public class ContentHasherTests : IDisposable
{
    private readonly IQmdDatabase db;
    private readonly DocumentRepository docRepo;

    public ContentHasherTests()
    {
        this.db = TestDbHelper.CreateInMemoryDb();
        this.docRepo = new DocumentRepository(this.db);
    }

    public void Dispose() => this.db.Dispose();

    [Fact]
    public void HashContent_ProducesSha256Hex()
    {
        var hash = ContentHasher.HashContent("Hello world");
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[a-f0-9]+$");
    }

    [Fact]
    public void HashContent_SameInputSameOutput()
    {
        var h1 = ContentHasher.HashContent("test content");
        var h2 = ContentHasher.HashContent("test content");
        h1.Should().Be(h2);
    }

    [Fact]
    public void HashContent_DifferentInputDifferentOutput()
    {
        var h1 = ContentHasher.HashContent("content A");
        var h2 = ContentHasher.HashContent("content B");
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void InsertContent_InsertsRow()
    {
        this.docRepo.InsertContent("abc123", "Hello", "2025-01-01");
        var row = this.db.Prepare("SELECT doc FROM content WHERE hash = $1").Get<DocRow>("abc123");
        row.Should().NotBeNull();
        row!.Doc.Should().Be("Hello");
    }

    [Fact]
    public void InsertContent_IgnoresDuplicate()
    {
        this.docRepo.InsertContent("abc123", "First", "2025-01-01");
        this.docRepo.InsertContent("abc123", "Second", "2025-01-02");
        var row = this.db.Prepare("SELECT doc FROM content WHERE hash = $1").Get<DocRow>("abc123");
        row!.Doc.Should().Be("First"); // First insert wins
    }

    private class DocRow
    {
        public string Doc { get; set; } = "";
    }
}
