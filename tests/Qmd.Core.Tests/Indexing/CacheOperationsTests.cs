using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Indexing;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Indexing;

[Trait("Category", "Database")]
public class CacheOperationsTests : IDisposable
{
    private readonly IQmdDatabase _db;

    public CacheOperationsTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void SetAndGetCachedResult()
    {
        var key = CacheOperations.GetCacheKey("test-url", new { query = "hello" });
        CacheOperations.SetCachedResult(_db, key, "cached result");
        CacheOperations.GetCachedResult(_db, key).Should().Be("cached result");
    }

    [Fact]
    public void GetCachedResult_ReturnsNull_WhenMissing()
    {
        CacheOperations.GetCachedResult(_db, "nonexistent").Should().BeNull();
    }

    [Fact]
    public void ClearCache_RemovesAll()
    {
        var key = CacheOperations.GetCacheKey("url", "body");
        CacheOperations.SetCachedResult(_db, key, "result");
        CacheOperations.ClearCache(_db);
        CacheOperations.GetCachedResult(_db, key).Should().BeNull();
    }

    [Fact]
    public void GetCacheKey_GeneratesConsistentKeys()
    {
        var key1 = CacheOperations.GetCacheKey("http://example.com", new { query = "test" });
        var key2 = CacheOperations.GetCacheKey("http://example.com", new { query = "test" });
        key1.Should().Be(key2);
        key1.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void GetCacheKey_GeneratesDifferentKeysForDifferentInputs()
    {
        var key1 = CacheOperations.GetCacheKey("http://example.com", new { query = "test1" });
        var key2 = CacheOperations.GetCacheKey("http://example.com", new { query = "test2" });
        key1.Should().NotBe(key2);
    }
}
