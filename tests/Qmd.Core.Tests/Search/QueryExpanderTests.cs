using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Search;
using Qmd.Core.Tests.Llm;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Search;

[Trait("Category", "Database")]
public class QueryExpanderTests : IDisposable
{
    private readonly IQmdDatabase _db;
    private readonly MockLlmService _llm;
    private readonly QueryExpanderService _expander;

    public QueryExpanderTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
        _llm = new MockLlmService();
        _expander = new QueryExpanderService(_db, _llm);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ExpandQuery_ReturnsExpansions()
    {
        var results = await _expander.ExpandQueryAsync("test query");
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExpandQuery_CachesResult()
    {
        await _expander.ExpandQueryAsync("cached query");
        // Second call should hit cache
        await _expander.ExpandQueryAsync("cached query");

        var cacheCount = _db.Prepare("SELECT COUNT(*) as cnt FROM llm_cache").GetDynamic();
        Convert.ToInt64(cacheCount!["cnt"]).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExpandQuery_DifferentQueriesDifferentCache()
    {
        await _expander.ExpandQueryAsync("query A");
        await _expander.ExpandQueryAsync("query B");

        var cacheCount = _db.Prepare("SELECT COUNT(*) as cnt FROM llm_cache").GetDynamic();
        Convert.ToInt64(cacheCount!["cnt"]).Should().Be(2);
    }
}
