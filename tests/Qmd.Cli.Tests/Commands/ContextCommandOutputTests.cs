using FluentAssertions;
using Qmd.Cli.Commands;
using Qmd.Core;
using Qmd.Core.Configuration;

namespace Qmd.Cli.Tests.Commands;

[Collection("ConsoleOutput")]
[Trait("Category", "Unit")]
public class ContextCommandOutputTests : IDisposable
{
    private readonly TestConsoleOutput _console = new();
    private readonly IConsoleOutput _original;

    public ContextCommandOutputTests()
    {
        _original = CliContext.Console;
        CliContext.Console = _console;
    }

    public void Dispose() => CliContext.Console = _original;

    private static async Task<IQmdStore> CreateStoreWithCollection()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/test/docs", Pattern = "**/*.md" },
            }
        };
        return await QmdStoreFactory.CreateInMemoryAsync(config);
    }

    [Fact]
    public async Task Add_Global_WritesConfirmation()
    {
        await using var store = await CreateStoreWithCollection();

        await ContextCommand.HandleAddAsync(store, "/", "Global system context");

        _console.GetOutput().Should().Contain("Global context set: Global system context");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public async Task Add_ToVirtualPath_WritesConfirmation()
    {
        await using var store = await CreateStoreWithCollection();

        await ContextCommand.HandleAddAsync(store, "qmd://docs/notes", "Meeting notes folder");

        _console.GetOutput().Should().Contain("Context added to docs:notes");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public async Task Add_NoCollections_WritesError()
    {
        await using var store = await QmdStoreFactory.CreateInMemoryAsync();

        await ContextCommand.HandleAddAsync(store, "/", "Some context");

        // Global "/" still hits collections check first
        _console.GetError().Should().Contain("No collections found.");
    }

    [Fact]
    public async Task Remove_Global_WritesConfirmation()
    {
        await using var store = await CreateStoreWithCollection();
        await store.SetGlobalContextAsync("to be removed");
        _console.Clear();

        await ContextCommand.HandleRemoveAsync(store, "/");

        _console.GetOutput().Should().Contain("Global context removed.");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_VirtualPath_Success_WritesConfirmation()
    {
        await using var store = await CreateStoreWithCollection();
        // VirtualPaths.Parse("qmd://docs/notes") yields path "notes" (no leading slash)
        await store.AddContextAsync("docs", "notes", "Context to remove");
        _console.Clear();

        await ContextCommand.HandleRemoveAsync(store, "qmd://docs/notes");

        _console.GetOutput().Should().Contain("Context removed.");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_VirtualPath_NotFound_WritesNotFound()
    {
        await using var store = await CreateStoreWithCollection();

        await ContextCommand.HandleRemoveAsync(store, "qmd://docs/nonexistent");

        _console.GetOutput().Should().Contain("Context not found.");
    }

    [Fact]
    public async Task Remove_NoCollections_WritesError()
    {
        await using var store = await QmdStoreFactory.CreateInMemoryAsync();

        await ContextCommand.HandleRemoveAsync(store, "qmd://docs/path");

        _console.GetError().Should().Contain("No collections found.");
    }
}
