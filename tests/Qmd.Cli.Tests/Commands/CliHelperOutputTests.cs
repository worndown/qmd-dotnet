using FluentAssertions;
using Qmd.Cli.Commands;
using Qmd.Cli.Formatting;

namespace Qmd.Cli.Tests.Commands;

[Collection("ConsoleOutput")]
[Trait("Category", "Unit")]
public class CliHelperOutputTests : IDisposable
{
    private readonly TestConsoleOutput console = new();
    private readonly IConsoleOutput original;

    public CliHelperOutputTests()
    {
        this.original = CliContext.Console;
        CliContext.Console = this.console;
    }

    public void Dispose() => CliContext.Console = this.original;

    [Fact]
    public void PrintEmpty_Json_WritesEmptyArray()
    {
        CliHelper.PrintEmptySearchResults(OutputFormat.Json);

        this.console.GetOutput().Should().Be("[]");
        this.console.GetError().Should().BeEmpty();
    }

    [Fact]
    public void PrintEmpty_Csv_WritesHeader()
    {
        CliHelper.PrintEmptySearchResults(OutputFormat.Csv);

        this.console.GetOutput().Should().Be("docid,score,file,title,context,line,snippet");
        this.console.GetError().Should().BeEmpty();
    }

    [Fact]
    public void PrintEmpty_Xml_WritesEmptyContainer()
    {
        CliHelper.PrintEmptySearchResults(OutputFormat.Xml);

        this.console.GetOutput().Should().Be("<results></results>");
        this.console.GetError().Should().BeEmpty();
    }

    [Fact]
    public void PrintEmpty_Cli_WritesNoResultsToStderr()
    {
        CliHelper.PrintEmptySearchResults(OutputFormat.Cli);

        this.console.GetOutput().Should().BeEmpty();
        this.console.GetError().Should().Contain("No results found.");
    }

    [Fact]
    public void PrintEmpty_Cli_WithScoreHint_WritesHintToStderr()
    {
        CliHelper.PrintEmptySearchResults(OutputFormat.Cli, "No results above --min-score 0.5.");

        this.console.GetOutput().Should().BeEmpty();
        this.console.GetError().Should().Contain("No results above --min-score 0.5.");
    }

    [Fact]
    public void PrintEmpty_Md_WritesNothing()
    {
        CliHelper.PrintEmptySearchResults(OutputFormat.Md);

        this.console.GetOutput().Should().BeEmpty();
        this.console.GetError().Should().BeEmpty();
    }

    [Fact]
    public void PrintEmpty_Files_WritesNothing()
    {
        CliHelper.PrintEmptySearchResults(OutputFormat.Files);

        this.console.GetOutput().Should().BeEmpty();
        this.console.GetError().Should().BeEmpty();
    }
}
