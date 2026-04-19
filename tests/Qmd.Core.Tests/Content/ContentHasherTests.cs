using FluentAssertions;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Content;

[Trait("Category", "Database")]
public class ContentHasherTests : IDisposable
{
    private readonly IQmdDatabase _db;
    private readonly DocumentRepository _docRepo;

    public ContentHasherTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
        _docRepo = new DocumentRepository(_db);
    }

    public void Dispose() => _db.Dispose();

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
        _docRepo.InsertContent("abc123", "Hello", "2025-01-01");
        var row = _db.Prepare("SELECT doc FROM content WHERE hash = $1").Get<DocRow>("abc123");
        row.Should().NotBeNull();
        row!.Doc.Should().Be("Hello");
    }

    [Fact]
    public void InsertContent_IgnoresDuplicate()
    {
        _docRepo.InsertContent("abc123", "First", "2025-01-01");
        _docRepo.InsertContent("abc123", "Second", "2025-01-02");
        var row = _db.Prepare("SELECT doc FROM content WHERE hash = $1").Get<DocRow>("abc123");
        row!.Doc.Should().Be("First"); // First insert wins
    }

    private class DocRow
    {
        public string Doc { get; set; } = "";
    }
}
