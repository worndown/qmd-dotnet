using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Store;

namespace Qmd.Core.Tests;

[Trait("Category", "Unit")]
public class ErrorHandlingTests : IDisposable
{
    private readonly QmdStore store;

    public ErrorHandlingTests()
    {
        this.store = new QmdStore(new SqliteDatabase(":memory:"));
    }

    public void Dispose() => this.store.Dispose();

    [Fact]
    public async Task SearchAsync_ThrowsQmdModelException_WhenLlmNotConfigured()
    {
        var act = async () => await this.store.SearchAsync(new SearchOptions { Query = "test" }, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<QmdModelException>()
            .WithMessage("*LLM service not configured*");
    }

    [Fact]
    public async Task EmbedAsync_ThrowsQmdModelException_WhenLlmNotConfigured()
    {
        var act = async () => await this.store.EmbedAsync(ct: TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<QmdModelException>()
            .WithMessage("*LLM service not configured*");
    }

    [Fact]
    public async Task ExpandQueryAsync_ThrowsQmdModelException_WhenLlmNotConfigured()
    {
        var act = async () => await this.store.ExpandQueryAsync("test", ct: TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<QmdModelException>()
            .WithMessage("*LLM service not configured*");
    }

    [Fact]
    public void QmdException_Hierarchy()
    {
        var modelEx = new QmdModelException("model error");
        var queryEx = new QmdQueryException("query error");

        modelEx.Should().BeAssignableTo<QmdException>();
        queryEx.Should().BeAssignableTo<QmdException>();
        modelEx.Should().BeAssignableTo<Exception>();
    }

    [Fact]
    public void QmdModelException_PreservesInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new QmdModelException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
        ex.Message.Should().Be("outer");
    }
}
