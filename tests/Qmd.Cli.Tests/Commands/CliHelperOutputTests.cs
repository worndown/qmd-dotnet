using FluentAssertions;
using Qmd.Cli.Commands;
using Qmd.Cli.Formatting;

namespace Qmd.Cli.Tests.Commands;

[Collection("ConsoleOutput")]
[Trait("Category", "Unit")]
public class CliHelperOutputTests : IDisposable
{
    private readonly TestConsoleOutput _console = new();
    private readonly IConsoleOutput _original;

    public CliHelperOutputTests()
    {
        _original = CliContext.Console;
        CliContext.Console = _console;
    }

    public void Dispose() => CliContext.Console = _original;

    [Fact]
    public void PrintEmpty_Json_WritesEmptyArray()
    {
        CliHelper.PrintEmptySearchResults(OutputFormat.Json);

        _console.GetOutput().Should().Be("[]");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public void PrintEmpty_Csv_WritesHeader()
    {
        CliHelper.PrintEmptySearchResults(OutputFormat.Csv);

        _console.GetOutput().Should().Be("docid,score,file,title,context,line,snippet");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public void PrintEmpty_Xml_WritesEmptyContainer()
    {
        CliHelper.PrintEmptySearchResults(OutputFormat.Xml);

        _console.GetOutput().Should().Be("<results></results>");
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public void PrintEmpty_Cli_WritesNoResultsToStderr()
    {
        CliHelper.PrintEmptySearchResults(OutputFormat.Cli);

        _console.GetOutput().Should().BeEmpty();
        _console.GetError().Should().Contain("No results found.");
    }

    [Fact]
    public void PrintEmpty_Cli_WithScoreHint_WritesHintToStderr()
    {
        CliHelper.PrintEmptySearchResults(OutputFormat.Cli, "No results above --min-score 0.5.");

        _console.GetOutput().Should().BeEmpty();
        _console.GetError().Should().Contain("No results above --min-score 0.5.");
    }

    [Fact]
    public void PrintEmpty_Md_WritesNothing()
    {
        CliHelper.PrintEmptySearchResults(OutputFormat.Md);

        _console.GetOutput().Should().BeEmpty();
        _console.GetError().Should().BeEmpty();
    }

    [Fact]
    public void PrintEmpty_Files_WritesNothing()
    {
        CliHelper.PrintEmptySearchResults(OutputFormat.Files);

        _console.GetOutput().Should().BeEmpty();
        _console.GetError().Should().BeEmpty();
    }
}
