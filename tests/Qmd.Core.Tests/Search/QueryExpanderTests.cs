using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Search;
using Qmd.Core.Tests.Llm;

namespace Qmd.Core.Tests.Search;

public class QueryExpanderTests : IDisposable
{
    private readonly SqliteDatabase _db;
    private readonly MockLlmService _llm;

    public QueryExpanderTests()
    {
        _db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(_db);
        _llm = new MockLlmService();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ExpandQuery_ReturnsExpansions()
    {
        var results = await QueryExpander.ExpandQueryAsync(_db, _llm, "test query");
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExpandQuery_CachesResult()
    {
        await QueryExpander.ExpandQueryAsync(_db, _llm, "cached query");
        // Second call should hit cache
        await QueryExpander.ExpandQueryAsync(_db, _llm, "cached query");

        var cacheCount = _db.Prepare("SELECT COUNT(*) as cnt FROM llm_cache").GetDynamic();
        Convert.ToInt64(cacheCount!["cnt"]).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExpandQuery_DifferentQueriesDifferentCache()
    {
        await QueryExpander.ExpandQueryAsync(_db, _llm, "query A");
        await QueryExpander.ExpandQueryAsync(_db, _llm, "query B");

        var cacheCount = _db.Prepare("SELECT COUNT(*) as cnt FROM llm_cache").GetDynamic();
        Convert.ToInt64(cacheCount!["cnt"]).Should().Be(2);
    }
}
