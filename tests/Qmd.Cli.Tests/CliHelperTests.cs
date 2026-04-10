using FluentAssertions;
using Qmd.Cli.Commands;
using Qmd.Core.Models;

namespace Qmd.Cli.Tests;

public class CliHelperTests
{
    [Theory]
    [InlineData("json", OutputFormat.Json)]
    [InlineData("csv", OutputFormat.Csv)]
    [InlineData("md", OutputFormat.Md)]
    [InlineData("markdown", OutputFormat.Md)]
    [InlineData("xml", OutputFormat.Xml)]
    [InlineData("files", OutputFormat.Files)]
    [InlineData("cli", OutputFormat.Cli)]
    [InlineData("unknown", OutputFormat.Cli)]
    public void ParseFormat_MapsCorrectly(string input, OutputFormat expected)
    {
        CliHelper.ParseFormat(input).Should().Be(expected);
    }

    [Fact]
    public void ParseStructuredQuery_ReturnsNull_ForPlainText()
    {
        CliHelper.ParseStructuredQuery("simple query").Should().BeNull();
    }

    [Fact]
    public void ParseStructuredQuery_ParsesLexPrefix()
    {
        var result = CliHelper.ParseStructuredQuery("lex: keyword search terms");
        result.Should().NotBeNull();
        result!.Queries.Should().HaveCount(1);
        result.Queries[0].Type.Should().Be("lex");
        result.Queries[0].Query.Should().Be("keyword search terms");
    }

    [Fact]
    public void ParseStructuredQuery_ParsesMultiplePrefixes()
    {
        var query = "lex: keyword search\nvec: semantic meaning\nhyde: hypothetical document";
        var result = CliHelper.ParseStructuredQuery(query);
        result.Should().NotBeNull();
        result!.Queries.Should().HaveCount(3);
        result.Queries[0].Type.Should().Be("lex");
        result.Queries[1].Type.Should().Be("vec");
        result.Queries[2].Type.Should().Be("hyde");
    }

    [Fact]
    public void ParseStructuredQuery_ExtractsIntent()
    {
        var query = "lex: API endpoints\nintent: REST API design";
        var result = CliHelper.ParseStructuredQuery(query);
        result.Should().NotBeNull();
        result!.Intent.Should().Be("REST API design");
        result.Queries.Should().HaveCount(1);
    }

    [Fact]
    public void ParseStructuredQuery_CaseInsensitive()
    {
        var result = CliHelper.ParseStructuredQuery("LEX: test\nVEC: semantic");
        result.Should().NotBeNull();
        result!.Queries.Should().HaveCount(2);
    }

    [Fact]
    public void ParseStructuredQuery_ThrowsOnIntentOnly()
    {
        // Only intent, no actual queries — TS throws an error
        var act = () => CliHelper.ParseStructuredQuery("intent: context only");
        act.Should().Throw<ArgumentException>().WithMessage("*cannot appear alone*");
    }

    // =========================================================================
    // ParseStructuredQuery — plain queries (ported from TS)
    // =========================================================================

    [Fact]
    public void ParseStructuredQuery_ExplicitExpandLine_ReturnsNull()
    {
        // "explicit expand line returns null" — expand: some query → null
        CliHelper.ParseStructuredQuery("expand: error handling best practices").Should().BeNull();
    }

    // =========================================================================
    // ParseStructuredQuery — error cases (ported from TS)
    // =========================================================================

    [Fact]
    public void ParseStructuredQuery_PlainLineWithPrefixedLines_Throws()
    {
        // "plain line with prefixed lines throws" — mixing plain + prefixed
        var act = () => CliHelper.ParseStructuredQuery("hello\nlex: world");
        act.Should().Throw<ArgumentException>().WithMessage("*missing a lex:/vec:/hyde:*");
    }

    [Fact]
    public void ParseStructuredQuery_MultiplePlainLines_Throws()
    {
        // "multiple plain lines throws"
        var act = () => CliHelper.ParseStructuredQuery("line one\nline two");
        act.Should().Throw<ArgumentException>().WithMessage("*missing a lex:/vec:/hyde:*");
    }

    [Fact]
    public void ParseStructuredQuery_ExpandWithoutText_Throws()
    {
        // "expand: without text throws"
        var act = () => CliHelper.ParseStructuredQuery("expand:   ");
        act.Should().Throw<ArgumentException>().WithMessage("*must include text*");
    }

    [Fact]
    public void ParseStructuredQuery_TypedLineWithoutText_Throws()
    {
        // "typed line without text throws" — lex: with no query after
        var act = () => CliHelper.ParseStructuredQuery("lex:   \nvec: real");
        act.Should().Throw<ArgumentException>().WithMessage("*must include text*");
    }

    // =========================================================================
    // ParseStructuredQuery — whitespace handling (ported from TS)
    // =========================================================================

    [Fact]
    public void ParseStructuredQuery_EmptyLinesIgnored()
    {
        // "empty lines ignored" — lex: hello\n\nvec: world still works
        var result = CliHelper.ParseStructuredQuery("lex: hello\n\nvec: world");
        result.Should().NotBeNull();
        result!.Queries.Should().HaveCount(2);
        result.Queries[0].Type.Should().Be("lex");
        result.Queries[0].Query.Should().Be("hello");
        result.Queries[1].Type.Should().Be("vec");
        result.Queries[1].Query.Should().Be("world");
    }

    [Fact]
    public void ParseStructuredQuery_WhitespaceOnlyLinesIgnored()
    {
        // "whitespace-only lines ignored"
        var result = CliHelper.ParseStructuredQuery("lex: hello\n   \nvec: world");
        result.Should().NotBeNull();
        result!.Queries.Should().HaveCount(2);
        result.Queries[0].Query.Should().Be("hello");
        result.Queries[1].Query.Should().Be("world");
    }

    [Fact]
    public void ParseStructuredQuery_LeadingTrailingWhitespaceTrimmed()
    {
        // "leading/trailing whitespace trimmed"
        var result = CliHelper.ParseStructuredQuery("  lex: keywords  \n  vec: question  ");
        result.Should().NotBeNull();
        result!.Queries.Should().HaveCount(2);
        result.Queries[0].Query.Should().Be("keywords");
        result.Queries[1].Query.Should().Be("question");
    }

    [Fact]
    public void ParseStructuredQuery_EmptyPrefixValue_Throws()
    {
        // "empty prefix value throws" — lex: (whitespace only after prefix)
        var act = () => CliHelper.ParseStructuredQuery("lex: \nvec: actual query");
        act.Should().Throw<ArgumentException>().WithMessage("*must include text*");
    }

    // =========================================================================
    // ParseStructuredQuery — edge cases (ported from TS)
    // =========================================================================

    [Fact]
    public void ParseStructuredQuery_ColonInQueryTextPreserved()
    {
        // "colon in query text preserved" — lex: what is this: a test
        var result = CliHelper.ParseStructuredQuery("lex: what is this: a test");
        result.Should().NotBeNull();
        result!.Queries.Should().HaveCount(1);
        result.Queries[0].Query.Should().Be("what is this: a test");
    }

    [Fact]
    public void ParseStructuredQuery_PrefixLikeTextInQueryPreserved()
    {
        // "prefix-like text in query preserved" — lex: the vec: protocol
        var result = CliHelper.ParseStructuredQuery("lex: the vec: protocol");
        result.Should().NotBeNull();
        result!.Queries.Should().HaveCount(1);
        result.Queries[0].Query.Should().Be("the vec: protocol");
    }

    // =========================================================================
    // ResolveCollections
    // =========================================================================

    // =========================================================================
    // ParseStructuredQuery — intent edge cases (ported from TS intent.test.ts)
    // =========================================================================

    [Fact]
    public void ParseStructuredQuery_IntentWithMultipleTypedLines()
    {
        var result = CliHelper.ParseStructuredQuery(
            "intent: web page load times\nlex: performance\nvec: how to improve performance");
        result.Should().NotBeNull();
        result!.Intent.Should().Be("web page load times");
        result.Queries.Should().HaveCount(2);
        result.Queries[0].Type.Should().Be("lex");
        result.Queries[1].Type.Should().Be("vec");
    }

    [Fact]
    public void ParseStructuredQuery_IntentAfterTypedLines()
    {
        var result = CliHelper.ParseStructuredQuery(
            "lex: performance\nintent: web page load times\nvec: latency");
        result.Should().NotBeNull();
        result!.Intent.Should().Be("web page load times");
        result.Queries.Should().HaveCount(2);
    }

    [Fact]
    public void ParseStructuredQuery_MultipleIntentLines_Throws()
    {
        var act = () => CliHelper.ParseStructuredQuery(
            "intent: web perf\nintent: team health\nlex: performance");
        act.Should().Throw<ArgumentException>().WithMessage("*only one intent*");
    }

    [Fact]
    public void ParseStructuredQuery_EmptyIntentText_Throws()
    {
        var act = () => CliHelper.ParseStructuredQuery("intent:\nlex: performance");
        act.Should().Throw<ArgumentException>().WithMessage("*intent: must include text*");
    }

    [Fact]
    public void ParseStructuredQuery_IntentWithWhitespaceOnlyText_Throws()
    {
        var act = () => CliHelper.ParseStructuredQuery("intent:   \nlex: performance");
        act.Should().Throw<ArgumentException>().WithMessage("*intent: must include text*");
    }

    [Fact]
    public void ParseStructuredQuery_IntentWithExpandThrows()
    {
        var act = () => CliHelper.ParseStructuredQuery("intent: web\nexpand: performance");
        act.Should().Throw<ArgumentException>().WithMessage("*cannot mix expand*");
    }

    [Fact]
    public void ParseStructuredQuery_IntentWithBlankLinesIsFine()
    {
        var result = CliHelper.ParseStructuredQuery(
            "intent: web perf\n\nlex: performance\n\nvec: speed");
        result.Should().NotBeNull();
        result!.Intent.Should().Be("web perf");
        result.Queries.Should().HaveCount(2);
    }

    [Fact]
    public void ParseStructuredQuery_IntentPreservesFullTextIncludingColons()
    {
        var result = CliHelper.ParseStructuredQuery(
            "intent: web performance: LCP, FID, CLS\nlex: performance");
        result.Should().NotBeNull();
        result!.Intent.Should().Be("web performance: LCP, FID, CLS");
    }

    // =========================================================================
    // ResolveFormat
    // =========================================================================

    [Theory]
    [InlineData(false, false, false, false, false, OutputFormat.Cli)]
    [InlineData(true, false, false, false, false, OutputFormat.Json)]
    [InlineData(false, true, false, false, false, OutputFormat.Csv)]
    [InlineData(false, false, true, false, false, OutputFormat.Md)]
    [InlineData(false, false, false, true, false, OutputFormat.Xml)]
    [InlineData(false, false, false, false, true, OutputFormat.Files)]
    public void ResolveFormat_BooleanFlagOverridesDefault(
        bool json, bool csv, bool md, bool xml, bool files, OutputFormat expected)
    {
        CliHelper.ResolveFormat("cli", json, csv, md, xml, files).Should().Be(expected);
    }

    [Theory]
    [InlineData("json", OutputFormat.Json)]
    [InlineData("csv", OutputFormat.Csv)]
    [InlineData("md", OutputFormat.Md)]
    [InlineData("xml", OutputFormat.Xml)]
    [InlineData("files", OutputFormat.Files)]
    public void ResolveFormat_FallsBackToFormatString(string format, OutputFormat expected)
    {
        CliHelper.ResolveFormat(format, false, false, false, false, false).Should().Be(expected);
    }

    [Fact]
    public void ResolveFormat_BooleanFlagTakesPrecedenceOverFormatString()
    {
        // --format csv with --json flag → JSON wins
        CliHelper.ResolveFormat("csv", json: true, csv: false, md: false, xml: false, files: false)
            .Should().Be(OutputFormat.Json);
    }

    // =========================================================================
    // ResolveCollections
    // =========================================================================

    [Fact]
    public async Task ResolveCollections_UsesProvidedCollections()
    {
        await using var store = await Qmd.Sdk.QmdStoreFactory.CreateInMemoryAsync();
        var result = await CliHelper.ResolveCollectionsAsync(store, ["docs", "code"]);
        result.Should().Equal("docs", "code");
    }

    [Fact]
    public async Task ResolveCollections_ReturnsNull_WhenNoCollections()
    {
        await using var store = await Qmd.Sdk.QmdStoreFactory.CreateInMemoryAsync();
        var result = await CliHelper.ResolveCollectionsAsync(store, []);
        // No collections configured → returns null (search all)
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveCollections_DefaultsToIncludedCollections()
    {
        var config = new Qmd.Core.Configuration.CollectionConfig
        {
            Collections = new()
            {
                ["a"] = new Qmd.Core.Configuration.Collection { Path = "/a" },
                ["b"] = new Qmd.Core.Configuration.Collection { Path = "/b", IncludeByDefault = false },
            }
        };
        await using var store = await Qmd.Sdk.QmdStoreFactory.CreateInMemoryAsync(config);
        var result = await CliHelper.ResolveCollectionsAsync(store, []);
        result.Should().Contain("a");
        result.Should().NotContain("b");
    }
}
