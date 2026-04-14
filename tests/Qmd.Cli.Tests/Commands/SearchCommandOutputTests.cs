using FluentAssertions;
using Qmd.Cli.Commands;
using Qmd.Cli.Formatting;
using Qmd.Core;
using Qmd.Core.Configuration;
using Qmd.Core.Content;
using Qmd.Core.Store;

namespace Qmd.Cli.Tests.Commands;

[Collection("ConsoleOutput")]
[Trait("Category", "Unit")]
public class SearchCommandOutputTests : IAsyncLifetime, IDisposable
{
    private readonly TestConsoleOutput _console = new();
    private readonly IConsoleOutput _original;
    private IQmdStore _store = null!;

    public SearchCommandOutputTests()
    {
        _original = CliContext.Console;
        CliContext.Console = _console;
    }

    public async Task InitializeAsync()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/test/docs", Pattern = "**/*.md" },
            }
        };
        _store = await QmdStoreFactory.CreateInMemoryAsync(config);
        var core = (QmdStore)_store;

        var content = "# API Documentation\n\nSearch for documents using the API.\n";
        var hash = ContentHasher.HashContent(content);
        core.DocumentRepo.InsertContent(hash, content, "2025-01-01");
        var title = core.ExtractTitle(content, "api.md");
        core.DocumentRepo.InsertDocument("docs", "api.md", title, hash, "2025-01-01", "2025-01-01");
    }

    public async Task DisposeAsync() => await _store.DisposeAsync();

    public void Dispose() => CliContext.Console = _original;

    [Fact]
    public async Task Search_WithResults_Json_WritesJsonToStdout()
    {
        await SearchCommand.HandleSearchAsync(
            _store, "API", [], 10, 0, OutputFormat.Json, false, false);

        var output = _console.GetOutput();
        output.Should().NotBeEmpty();
        output.Should().Contain("api.md");
        // Should be valid JSON
        output.Trim().Should().StartWith("[");
    }

    [Fact]
    public async Task Search_WithResults_Csv_WritesHeaderAndData()
    {
        await SearchCommand.HandleSearchAsync(
            _store, "API", [], 10, 0, OutputFormat.Csv, false, false);

        var output = _console.GetOutput();
        output.Should().StartWith("docid,score,file,title,context,line,snippet");
        output.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public async Task Search_NoResults_WritesEmptyJson()
    {
        await SearchCommand.HandleSearchAsync(
            _store, "xyznonexistent123", [], 10, 0, OutputFormat.Json, false, false);

        _console.GetOutput().Should().Be("[]");
    }

    [Fact]
    public async Task Search_NoResults_Cli_WritesNoResultsToStderr()
    {
        await SearchCommand.HandleSearchAsync(
            _store, "xyznonexistent123", [], 10, 0, OutputFormat.Cli, false, false);

        _console.GetOutput().Should().BeEmpty();
        _console.GetError().Should().Contain("No results found.");
    }

    [Fact]
    public async Task Search_MinScore_FiltersResults()
    {
        // Use an extremely high min-score to filter out everything
        await SearchCommand.HandleSearchAsync(
            _store, "API", [], 10, 999.0, OutputFormat.Json, false, false);

        _console.GetOutput().Should().Be("[]");
    }
}
