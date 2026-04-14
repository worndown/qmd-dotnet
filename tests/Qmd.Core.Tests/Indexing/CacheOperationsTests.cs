using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Indexing;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Indexing;

[Trait("Category", "Database")]
public class CacheOperationsTests : IDisposable
{
    private readonly IQmdDatabase _db;
    private readonly CacheRepository _repo;

    public CacheOperationsTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
        _repo = new CacheRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void SetAndGetCachedResult()
    {
        var key = CacheRepository.GetCacheKey("test-url", new { query = "hello" });
        _repo.SetCachedResult(key, "cached result");
        _repo.GetCachedResult(key).Should().Be("cached result");
    }

    [Fact]
    public void GetCachedResult_ReturnsNull_WhenMissing()
    {
        _repo.GetCachedResult("nonexistent").Should().BeNull();
    }

    [Fact]
    public void ClearCache_RemovesAll()
    {
        var key = CacheRepository.GetCacheKey("url", "body");
        _repo.SetCachedResult(key, "result");
        _repo.ClearCache();
        _repo.GetCachedResult(key).Should().BeNull();
    }

    [Fact]
    public void GetCacheKey_GeneratesConsistentKeys()
    {
        var key1 = CacheRepository.GetCacheKey("http://example.com", new { query = "test" });
        var key2 = CacheRepository.GetCacheKey("http://example.com", new { query = "test" });
        key1.Should().Be(key2);
        key1.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void GetCacheKey_GeneratesDifferentKeysForDifferentInputs()
    {
        var key1 = CacheRepository.GetCacheKey("http://example.com", new { query = "test1" });
        var key2 = CacheRepository.GetCacheKey("http://example.com", new { query = "test2" });
        key1.Should().NotBe(key2);
    }
}
