using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Search;
using Qmd.Core.Tests.Llm;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Search;

[Trait("Category", "Database")]
public class QueryExpanderTests : IDisposable
{
    private readonly IQmdDatabase db;
    private readonly MockLlmService llm;
    private readonly QueryExpanderService expander;

    public QueryExpanderTests()
    {
        this.db = TestDbHelper.CreateInMemoryDb();
        this.llm = new MockLlmService();
        this.expander = new QueryExpanderService(this.db, this.llm);
    }

    public void Dispose() => this.db.Dispose();

    [Fact]
    public async Task ExpandQuery_ReturnsExpansions()
    {
        var results = await this.expander.ExpandQueryAsync("test query");
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExpandQuery_CachesResult()
    {
        await this.expander.ExpandQueryAsync("cached query");
        // Second call should hit cache
        await this.expander.ExpandQueryAsync("cached query");

        var cacheCount = this.db.Prepare("SELECT COUNT(*) as cnt FROM llm_cache").Get<CountRow>();
        cacheCount!.Cnt.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExpandQuery_DifferentQueriesDifferentCache()
    {
        await this.expander.ExpandQueryAsync("query A");
        await this.expander.ExpandQueryAsync("query B");

        var cacheCount = this.db.Prepare("SELECT COUNT(*) as cnt FROM llm_cache").Get<CountRow>();
        cacheCount!.Cnt.Should().Be(2);
    }
}
