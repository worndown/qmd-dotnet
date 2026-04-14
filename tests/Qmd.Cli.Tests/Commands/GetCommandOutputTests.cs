using FluentAssertions;
using Qmd.Cli.Commands;
using Qmd.Core;
using Qmd.Core.Configuration;
using Qmd.Core.Content;
using Qmd.Core.Store;

namespace Qmd.Cli.Tests.Commands;

[Collection("ConsoleOutput")]
[Trait("Category", "Unit")]
public class GetCommandOutputTests : IAsyncLifetime, IDisposable
{
    private readonly TestConsoleOutput _console = new();
    private readonly IConsoleOutput _original;
    private IQmdStore _store = null!;

    public GetCommandOutputTests()
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

        Seed(core, "docs", "readme.md",
            "# Test Project\n\nThis is a test project.\n\nLine 4.\nLine 5.\nLine 6.\nLine 7.\n");

        Seed(core, "docs", "notes/meeting.md",
            "# Meeting Notes\n\nDiscussion topics.\n");

        // Seed with context
        await _store.AddContextAsync("docs", "/notes", "Internal meeting notes");
    }

    public async Task DisposeAsync() => await _store.DisposeAsync();

    public void Dispose() => CliContext.Console = _original;

    private static void Seed(QmdStore core, string collection, string path, string content)
    {
        var hash = ContentHasher.HashContent(content);
        core.DocumentRepo.InsertContent(hash, content, "2025-01-01");
        var title = core.ExtractTitle(content, path);
        core.DocumentRepo.InsertDocument(collection, path, title, hash, "2025-01-01", "2025-01-01");
    }

    [Fact]
    public async Task Get_FoundDocument_OutputsHeaderAndBody()
    {
        await GetCommand.HandleGetAsync(_store, "readme.md", null, null, false);

        var output = _console.GetOutput();
        output.Should().Contain("# docs/readme.md");
        output.Should().Contain("This is a test project.");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public async Task Get_FoundDocument_WithContext_IncludesContextLine()
    {
        await GetCommand.HandleGetAsync(_store, "notes/meeting.md", null, null, false);

        var output = _console.GetOutput();
        output.Should().Contain("# docs/notes/meeting.md");
        output.Should().Contain("Context: Internal meeting notes");
        output.Should().Contain("Discussion topics.");
    }

    [Fact]
    public async Task Get_NotFound_WritesErrorToStderr()
    {
        await GetCommand.HandleGetAsync(_store, "nonexistent.md", null, null, false);

        _console.GetOutput().Should().BeEmpty();
        _console.GetError().Should().Contain("Not found: nonexistent.md");
    }

    [Fact]
    public async Task Get_NotFound_ShowsSimilarFiles()
    {
        // "readm.md" is similar to "readme.md"
        await GetCommand.HandleGetAsync(_store, "readm.md", null, null, false);

        var error = _console.GetError();
        error.Should().Contain("Not found: readm.md");
        error.Should().Contain("Similar files:");
        error.Should().Contain("readme.md");
    }

    [Fact]
    public async Task Get_WithFromAndLines_SlicesBody()
    {
        // Content: "# Test Project\n\nThis is a test project.\n\nLine 4.\nLine 5.\nLine 6.\nLine 7.\n"
        // Lines:    1: "# Test Project"  2: ""  3: "This is a test project."  4: ""  5: "Line 4."  6: "Line 5."
        await GetCommand.HandleGetAsync(_store, "readme.md", 5, 2, false);

        var output = _console.GetOutput();
        output.Should().Contain("Line 4.");
        output.Should().Contain("Line 5.");
        output.Should().NotContain("# Test Project");
        output.Should().NotContain("Line 6.");
    }

    [Fact]
    public async Task Get_WithLineNumbers_FormatsWithNumbers()
    {
        await GetCommand.HandleGetAsync(_store, "readme.md", null, null, true);

        var output = _console.GetOutput();
        // FormatHelpers.AddLineNumbers prepends line numbers like "1: ..."
        output.Should().MatchRegex(@"\d+:");
    }
}
