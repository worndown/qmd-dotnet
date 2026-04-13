using FluentAssertions;
using Qmd.Core.Content;
using Qmd.Core.Database;

namespace Qmd.Core.Tests.Content;

public class ContentHasherTests : IDisposable
{
    private readonly SqliteDatabase _db;

    public ContentHasherTests()
    {
        _db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(_db);
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
        ContentHasher.InsertContent(_db, "abc123", "Hello", "2025-01-01");
        var row = _db.Prepare("SELECT doc FROM content WHERE hash = $1").GetDynamic("abc123");
        row.Should().NotBeNull();
        row!["doc"].Should().Be("Hello");
    }

    [Fact]
    public void InsertContent_IgnoresDuplicate()
    {
        ContentHasher.InsertContent(_db, "abc123", "First", "2025-01-01");
        ContentHasher.InsertContent(_db, "abc123", "Second", "2025-01-02");
        var row = _db.Prepare("SELECT doc FROM content WHERE hash = $1").GetDynamic("abc123");
        row!["doc"].Should().Be("First"); // First insert wins
    }
}
