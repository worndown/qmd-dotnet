using FluentAssertions;
using Qmd.Cli.Commands;
using Qmd.Core;
using Qmd.Core.Configuration;

namespace Qmd.Cli.Tests.Commands;

[Collection("ConsoleOutput")]
[Trait("Category", "Unit")]
public class CollectionCommandOutputTests : IDisposable
{
    private readonly TestConsoleOutput _console = new();
    private readonly IConsoleOutput _original;

    public CollectionCommandOutputTests()
    {
        _original = CliContext.Console;
        CliContext.Console = _console;
    }

    public void Dispose() => CliContext.Console = _original;

    private static async Task<IQmdStore> CreateStoreWithCollection(string name = "fixtures", string path = "/test/fixtures")
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                [name] = new Collection { Path = path, Pattern = "**/*.md" },
            }
        };
        return await QmdStoreFactory.CreateInMemoryAsync(config);
    }

    [Fact]
    public async Task Remove_Success_WritesConfirmation()
    {
        await using var store = await CreateStoreWithCollection();

        await CollectionCommand.HandleRemoveAsync(store, "fixtures");

        _console.GetOutput().Should().Contain("Collection 'fixtures' removed.");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_NotFound_WritesErrorToStderr()
    {
        await using var store = await CreateStoreWithCollection();

        await CollectionCommand.HandleRemoveAsync(store, "nonexistent");

        _console.GetOutput().Should().BeEmpty();
        _console.GetError().Should().Contain("Collection 'nonexistent' not found.");
    }

    [Fact]
    public async Task Rename_Success_WritesConfirmation()
    {
        await using var store = await CreateStoreWithCollection();

        await CollectionCommand.HandleRenameAsync(store, "fixtures", "renamed");

        _console.GetOutput().Should().Contain("Collection 'fixtures' renamed to 'renamed'.");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public async Task Rename_NotFound_WritesErrorToStderr()
    {
        await using var store = await CreateStoreWithCollection();

        await CollectionCommand.HandleRenameAsync(store, "nonexistent", "renamed");

        _console.GetOutput().Should().BeEmpty();
        _console.GetError().Should().Contain("Collection 'nonexistent' not found.");
    }

    [Fact]
    public async Task Show_DisplaysAllFields()
    {
        await using var store = await CreateStoreWithCollection();

        await CollectionCommand.HandleShowAsync(store, "fixtures");

        var output = _console.GetOutput();
        output.Should().Contain("Name:    fixtures");
        output.Should().Contain("Path:    /test/fixtures");
        output.Should().Contain("Pattern: **/*.md");
        output.Should().Contain("Include: yes");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public async Task Show_ExcludedCollection_ShowsNo()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["archive"] = new Collection { Path = "/test/archive", Pattern = "**/*.md", IncludeByDefault = false },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);

        await CollectionCommand.HandleShowAsync(store, "archive");

        _console.GetOutput().Should().Contain("Include: no");
    }

    [Fact]
    public async Task Show_NotFound_WritesErrorToStderr()
    {
        await using var store = await CreateStoreWithCollection();

        await CollectionCommand.HandleShowAsync(store, "nonexistent");

        _console.GetOutput().Should().BeEmpty();
        _console.GetError().Should().Contain("Collection 'nonexistent' not found.");
    }

    [Fact]
    public async Task UpdateCmd_Set_WritesConfirmation()
    {
        await using var store = await CreateStoreWithCollection();

        await CollectionCommand.HandleUpdateCmdAsync(store, "fixtures", "git pull");

        _console.GetOutput().Should().Contain("Update command set for 'fixtures': git pull");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateCmd_Clear_WritesConfirmation()
    {
        await using var store = await CreateStoreWithCollection();
        // First set a command
        await store.UpdateCollectionSettingsAsync("fixtures", update: "git pull");
        _console.Clear();

        await CollectionCommand.HandleUpdateCmdAsync(store, "fixtures", null);

        _console.GetOutput().Should().Contain("Update command cleared for 'fixtures'.");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateCmd_NotFound_WritesErrorToStderr()
    {
        await using var store = await CreateStoreWithCollection();

        await CollectionCommand.HandleUpdateCmdAsync(store, "nonexistent", "git pull");

        _console.GetOutput().Should().BeEmpty();
        _console.GetError().Should().Contain("Collection 'nonexistent' not found.");
    }

    [Fact]
    public async Task Include_Success_WritesConfirmation()
    {
        await using var store = await CreateStoreWithCollection();

        await CollectionCommand.HandleIncludeAsync(store, "fixtures");

        _console.GetOutput().Should().Contain("Collection 'fixtures' included in default searches.");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public async Task Include_NotFound_WritesErrorToStderr()
    {
        await using var store = await CreateStoreWithCollection();

        await CollectionCommand.HandleIncludeAsync(store, "nonexistent");

        _console.GetOutput().Should().BeEmpty();
        _console.GetError().Should().Contain("Collection 'nonexistent' not found.");
    }

    [Fact]
    public async Task Exclude_Success_WritesConfirmation()
    {
        await using var store = await CreateStoreWithCollection();

        await CollectionCommand.HandleExcludeAsync(store, "fixtures");

        _console.GetOutput().Should().Contain("Collection 'fixtures' excluded from default searches.");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public async Task Exclude_NotFound_WritesErrorToStderr()
    {
        await using var store = await CreateStoreWithCollection();

        await CollectionCommand.HandleExcludeAsync(store, "nonexistent");

        _console.GetOutput().Should().BeEmpty();
        _console.GetError().Should().Contain("Collection 'nonexistent' not found.");
    }
}
