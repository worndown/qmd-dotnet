using System.Text.Json;
using FluentAssertions;
using Qmd.Cli.Formatting;
using Qmd.Core.Models;

namespace Qmd.Cli.Tests.Formatting;

[Trait("Category", "Unit")]
public class FormatterTests
{
    private const string TestContext = "Internal engineering keynotes";

    private static SearchResult MakeSearchResult(string? context = null)
    {
        return new SearchResult
        {
            Filepath = "qmd://docs/api.md",
            DisplayPath = "docs/api.md",
            Title = "API Reference",
            Hash = "abc123def456",
            DocId = "abc123",
            CollectionName = "docs",
            ModifiedAt = "2025-01-01",
            BodyLength = 100,
            Body = "API documentation content",
            Context = context,
            Score = 0.85,
            Source = "fts",
        };
    }

    private static DocumentResult MakeDocumentResult(string? context = null)
    {
        return new DocumentResult
        {
            Filepath = "qmd://docs/readme.md",
            DisplayPath = "docs/readme.md",
            Title = "README",
            Hash = "def456abc123",
            DocId = "def456",
            CollectionName = "docs",
            ModifiedAt = "2025-01-01",
            BodyLength = 50,
            Body = "Readme content",
            Context = context,
        };
    }

    private static MultiGetFile MakeMultiGetFile(string? context = null)
    {
        return new MultiGetFile
        {
            Filepath = "qmd://docs/guide.md",
            DisplayPath = "docs/guide.md",
            Title = "Guide",
            Body = "Guide content",
            Context = context,
        };
    }

    [Fact]
    public void SearchResults_Json_IncludesContext()
    {
        var output = SearchResultFormatter.ToJson([MakeSearchResult(TestContext)]);
        output.Should().Contain(TestContext);
        var parsed = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(output)!;
        parsed[0].Should().ContainKey("context");
    }

    [Fact]
    public void SearchResults_Csv_IncludesContext()
    {
        var output = SearchResultFormatter.ToCsv([MakeSearchResult(TestContext)]);
        output.Should().Contain(TestContext);
    }

    [Fact]
    public void SearchResults_Files_IncludesContext()
    {
        var output = SearchResultFormatter.ToFiles([MakeSearchResult(TestContext)]);
        output.Should().Contain(TestContext);
    }

    [Fact]
    public void SearchResults_Markdown_IncludesContext()
    {
        var output = SearchResultFormatter.ToMarkdown([MakeSearchResult(TestContext)]);
        output.Should().Contain(TestContext);
    }

    [Fact]
    public void SearchResults_Xml_IncludesContext()
    {
        var output = SearchResultFormatter.ToXml([MakeSearchResult(TestContext)]);
        output.Should().Contain(TestContext);
    }

    [Fact]
    public void SearchResults_Json_OmitsNullContext()
    {
        var output = SearchResultFormatter.ToJson([MakeSearchResult(null)]);
        output.Should().NotContain("\"context\"");
    }

    [Fact]
    public void SearchResults_Files_NoTrailingComma_WhenNoContext()
    {
        var output = SearchResultFormatter.ToFiles([MakeSearchResult(null)]);
        output.TrimEnd().Should().NotEndWith(",");
    }

    [Theory]
    [InlineData(OutputFormat.Json)]
    [InlineData(OutputFormat.Csv)]
    [InlineData(OutputFormat.Md)]
    [InlineData(OutputFormat.Xml)]
    [InlineData(OutputFormat.Files)]
    public void SearchResults_FormatDispatcher_ProducesOutput(OutputFormat format)
    {
        var output = SearchResultFormatter.Format([MakeSearchResult(TestContext)], format);
        output.Should().NotBeNullOrEmpty();
        // All formats include either the display path or docid
        output.Should().Contain("abc123");
    }

    [Fact]
    public void Documents_Json_IncludesContext()
    {
        var output = DocumentFormatter.ToJson([MakeMultiGetFile(TestContext)]);
        output.Should().Contain(TestContext);
    }

    [Fact]
    public void Documents_Csv_IncludesContext()
    {
        var output = DocumentFormatter.ToCsv([MakeMultiGetFile(TestContext)]);
        output.Should().Contain(TestContext);
    }

    [Fact]
    public void Documents_Markdown_IncludesContext()
    {
        var output = DocumentFormatter.ToMarkdown([MakeMultiGetFile(TestContext)]);
        output.Should().Contain(TestContext);
    }

    [Fact]
    public void Documents_Xml_IncludesContext()
    {
        var output = DocumentFormatter.ToXml([MakeMultiGetFile(TestContext)]);
        output.Should().Contain(TestContext);
    }

    [Fact]
    public void SingleDoc_Json_IncludesContext()
    {
        var output = SingleDocumentFormatter.ToJson(MakeDocumentResult(TestContext));
        output.Should().Contain(TestContext);
    }

    [Fact]
    public void SingleDoc_Markdown_IncludesContext()
    {
        var output = SingleDocumentFormatter.ToMarkdown(MakeDocumentResult(TestContext));
        output.Should().Contain(TestContext);
    }

    [Fact]
    public void SingleDoc_Xml_IncludesContext()
    {
        var output = SingleDocumentFormatter.ToXml(MakeDocumentResult(TestContext));
        output.Should().Contain(TestContext);
    }

    [Fact]
    public void SingleDoc_Json_OmitsNullContext()
    {
        var output = SingleDocumentFormatter.ToJson(MakeDocumentResult(null));
        output.Should().NotContain("\"context\"");
    }

    [Fact]
    public void SingleDoc_Markdown_NoContextLine_WhenNull()
    {
        var output = SingleDocumentFormatter.ToMarkdown(MakeDocumentResult(null));
        output.Should().NotContain("**context:**");
    }

    [Fact]
    public void SingleDoc_Xml_NoContextAttr_WhenNull()
    {
        var output = SingleDocumentFormatter.ToXml(MakeDocumentResult(null));
        output.Should().NotContain("context=");
    }

    [Fact]
    public void EscapeCsv_WrapsFieldsWithCommas()
    {
        FormatHelpers.EscapeCsv("hello, world").Should().Be("\"hello, world\"");
    }

    [Fact]
    public void EscapeCsv_DoublesInternalQuotes()
    {
        FormatHelpers.EscapeCsv("say \"hello\"").Should().Be("\"say \"\"hello\"\"\"");
    }

    [Fact]
    public void EscapeXml_EscapesSpecialChars()
    {
        FormatHelpers.EscapeXml("<tag attr=\"val\">&").Should().Be("&lt;tag attr=&quot;val&quot;&gt;&amp;");
    }

    [Fact]
    public void AddLineNumbers_AddsCorrectNumbers()
    {
        var result = FormatHelpers.AddLineNumbers("line1\nline2\nline3");
        result.Should().Be("1: line1\n2: line2\n3: line3");
    }

    [Fact]
    public void AddLineNumbers_CustomStart()
    {
        var result = FormatHelpers.AddLineNumbers("a\nb", 10);
        result.Should().Be("10: a\n11: b");
    }

    [Fact]
    public void Markdown_ShowsSnippet_WhenQueryProvided()
    {
        var result = new SearchResult
        {
            Filepath = "qmd://docs/api.md",
            DisplayPath = "docs/api.md",
            Title = "API Reference",
            Hash = "abc123def456",
            DocId = "abc123",
            Body = "First line\nSecond line has API endpoints\nThird line\nFourth line",
            Score = 0.85,
            Source = "fts",
        };

        var output = SearchResultFormatter.ToMarkdown([result],
            new FormatOptions { Query = "API endpoints" });

        // Should contain snippet with @@ header (from SnippetExtractor)
        output.Should().Contain("@@");
        output.Should().Contain("API");
    }

    [Fact]
    public void Markdown_ShowsFullBody_WhenFullOptionSet()
    {
        var result = MakeSearchResult();
        var output = SearchResultFormatter.ToMarkdown([result],
            new FormatOptions { Full = true, Query = "API" });

        output.Should().Contain("API documentation content");
    }

    [Fact]
    public void Csv_ShowsSnippet_WhenQueryProvided()
    {
        var result = new SearchResult
        {
            Filepath = "qmd://docs/api.md",
            DisplayPath = "docs/api.md",
            Title = "API Reference",
            Hash = "abc123def456",
            DocId = "abc123",
            Body = "First line\nAPI endpoints section\nThird line",
            Score = 0.85,
            Source = "fts",
        };

        var output = SearchResultFormatter.ToCsv([result],
            new FormatOptions { Query = "API" });

        // Should contain snippet content, not truncated body
        output.Should().Contain("@@");
    }

    [Fact]
    public void Json_ShowsBody_WhenFullOptionSet()
    {
        var result = MakeSearchResult();
        var output = SearchResultFormatter.ToJson([result],
            new FormatOptions { Full = true });

        output.Should().Contain("API documentation content");
    }

    [Fact]
    public void Json_OmitsBody_WhenNotFull()
    {
        var result = MakeSearchResult();
        var output = SearchResultFormatter.ToJson([result],
            new FormatOptions { Full = false });

        // Body field should not be present (snippet field replaces it)
        output.Should().NotContain("\"body\"");
        output.Should().Contain("\"snippet\"");
    }

    [Fact]
    public void Json_IncludesLineNumbers_WhenFullAndLineNumbers()
    {
        var result = new SearchResult
        {
            Filepath = "qmd://docs/api.md",
            DisplayPath = "docs/api.md",
            Title = "API",
            Hash = "abc123def456",
            DocId = "abc123",
            Body = "line one\nline two",
            Score = 0.85,
            Source = "fts",
        };

        var output = SearchResultFormatter.ToJson([result],
            new FormatOptions { Full = true, LineNumbers = true });

        output.Should().Contain("1: line one");
        output.Should().Contain("2: line two");
    }

    [Fact]
    public void HighlightTerms_HighlightsMatchingTerms()
    {
        // Set NO_COLOR to empty to enable highlighting in tests
        Environment.SetEnvironmentVariable("NO_COLOR", null);
        var result = FormatHelpers.HighlightTerms("The API has multiple endpoints", "API endpoints");
        result.Should().Contain("\x1b[1;33m");  // ANSI bold yellow
        result.Should().Contain("API");
    }

    [Fact]
    public void HighlightTerms_ReturnsOriginal_WhenNoQuery()
    {
        var text = "some text";
        FormatHelpers.HighlightTerms(text, null).Should().Be(text);
        FormatHelpers.HighlightTerms(text, "").Should().Be(text);
    }

    [Fact]
    public void HighlightTerms_SkipsShortTerms()
    {
        Environment.SetEnvironmentVariable("NO_COLOR", null);
        var result = FormatHelpers.HighlightTerms("a is in the API", "a in API");
        // Only "API" should be highlighted (>2 chars), not "a" or "in"
        result.Should().Contain("\x1b[1;33mAPI\x1b[0m");
    }

    [Fact]
    public void MakeTerminalLink_CreatesOsc8Link()
    {
        Environment.SetEnvironmentVariable("NO_COLOR", null);
        var result = FormatHelpers.MakeTerminalLink("api.md", "vscode://file/{path}:{line}", "/docs/api.md", 42);
        result.Should().Contain("\x1b]8;;");
        result.Should().Contain("vscode://file//docs/api.md:42");
        result.Should().Contain("api.md");
    }

    [Fact]
    public void MakeTerminalLink_ReturnsDisplayText_WhenNoEditorUri()
    {
        FormatHelpers.MakeTerminalLink("api.md", null, "/path").Should().Be("api.md");
        FormatHelpers.MakeTerminalLink("api.md", "", "/path").Should().Be("api.md");
    }

    [Fact]
    public void Markdown_ShowsContent_WhenQueryIsEmpty()
    {
        var result = MakeSearchResult();
        var output = SearchResultFormatter.ToMarkdown([result],
            new FormatOptions { Query = "" });

        // TS always shows snippet even with empty query
        output.Should().Contain("API documentation content");
    }

    [Fact]
    public void Markdown_ShowsContent_WhenQueryIsNull()
    {
        var result = MakeSearchResult();
        var output = SearchResultFormatter.ToMarkdown([result],
            new FormatOptions { Query = null });

        output.Should().Contain("API documentation content");
    }

    [Theory]
    [InlineData(OutputFormat.Json)]
    [InlineData(OutputFormat.Csv)]
    [InlineData(OutputFormat.Md)]
    [InlineData(OutputFormat.Xml)]
    [InlineData(OutputFormat.Files)]
    public void Documents_FormatDispatcher_ProducesOutput(OutputFormat format)
    {
        var output = DocumentFormatter.Format([MakeMultiGetFile(TestContext)], format);
        output.Should().NotBeNullOrEmpty();
        output.Should().Contain("guide.md");
    }

    [Fact]
    public void Documents_Json_SkippedFileHasReasonNoBody()
    {
        var file = new MultiGetFile
        {
            DisplayPath = "big.md",
            Title = "Big",
            Skipped = true,
            SkipReason = "File too large",
        };
        var output = DocumentFormatter.ToJson([file]);
        output.Should().Contain("\"skipped\"");
        output.Should().Contain("\"reason\"");
        output.Should().NotContain("\"body\"");
    }

    [Fact]
    public void SearchResults_Json_IncludesLineNumber()
    {
        // JSON format includes line number
        var result = MakeSearchResult();
        result.Body = "line one\nline two\napi documentation\nline four";
        var json = SearchResultFormatter.ToJson([result], new FormatOptions { Query = "api" });
        var arr = JsonSerializer.Deserialize<JsonElement>(json);
        arr[0].TryGetProperty("line", out var line).Should().BeTrue();
        line.GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public void SearchResults_Json_WithFull_IncludesBody()
    {
        // JSON format includes body when --full is specified
        var result = MakeSearchResult();
        result.Body = "line one\nline two\napi documentation\nline four";
        var json = SearchResultFormatter.ToJson([result], new FormatOptions { Query = "api", Full = true });
        var arr = JsonSerializer.Deserialize<JsonElement>(json);
        arr[0].TryGetProperty("body", out var body).Should().BeTrue();
        body.GetString().Should().Contain("api documentation");
    }

    private static SearchResult MakeSearchResultWithExplain()
    {
        var result = MakeSearchResult();
        result.Explain = new HybridQueryExplain(
            FtsScores: [0.0425],
            VectorScores: [0.6725, 0.6831],
            Rrf: new RrfScoreTrace
            {
                BaseScore = 0.0814,
                TopRank = 1,
                TopRankBonus = 0.05,
                TotalScore = 0.1314,
                Contributions =
                [
                    new RrfContributionTrace(0, "fts", "original", "test", 2, 1.0, 0.0425, 0.0323),
                    new RrfContributionTrace(1, "vec", "original", "test", 1, 1.0, 0.6725, 0.0328),
                    new RrfContributionTrace(2, "vec", "hyde", "test", 1, 0.5, 0.6831, 0.0164),
                ],
            },
            RerankScore: 0.5096,
            BlendedScore: 0.8774
        );
        return result;
    }

    [Fact]
    public void SearchResults_Json_WithExplain_IncludesExplainField()
    {
        var result = MakeSearchResultWithExplain();
        var json = SearchResultFormatter.ToJson([result], new FormatOptions { Query = "test", Explain = true });
        var arr = JsonSerializer.Deserialize<JsonElement>(json);
        arr[0].TryGetProperty("explain", out var explain).Should().BeTrue();
        explain.TryGetProperty("ftsScores", out _).Should().BeTrue();
        explain.TryGetProperty("vectorScores", out _).Should().BeTrue();
        explain.TryGetProperty("rerankScore", out _).Should().BeTrue();
        explain.TryGetProperty("blendedScore", out _).Should().BeTrue();
        explain.TryGetProperty("rrf", out var rrf).Should().BeTrue();
        rrf.TryGetProperty("baseScore", out _).Should().BeTrue();
        rrf.TryGetProperty("contributions", out var contribs).Should().BeTrue();
        contribs.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void SearchResults_Json_WithoutExplainFlag_OmitsExplainField()
    {
        var result = MakeSearchResultWithExplain();
        var json = SearchResultFormatter.ToJson([result], new FormatOptions { Query = "test", Explain = false });
        var arr = JsonSerializer.Deserialize<JsonElement>(json);
        arr[0].TryGetProperty("explain", out _).Should().BeFalse();
    }

    [Fact]
    public void SearchResults_Markdown_WithExplain_IncludesTraceLines()
    {
        var result = MakeSearchResultWithExplain();
        var md = SearchResultFormatter.ToMarkdown([result], new FormatOptions { Query = "test", Explain = true });
        md.Should().Contain("Explain: fts=[0.0425] vec=[0.6725, 0.6831]");
        md.Should().Contain("RRF: total=");
        md.Should().Contain("Blend:");
        md.Should().Contain("Top RRF contributions:");
    }

    [Fact]
    public void SearchResults_Markdown_WithoutExplainFlag_OmitsTraceLines()
    {
        var result = MakeSearchResultWithExplain();
        var md = SearchResultFormatter.ToMarkdown([result], new FormatOptions { Query = "test", Explain = false });
        md.Should().NotContain("Explain:");
        md.Should().NotContain("RRF:");
    }

    [Fact]
    public void SearchResults_Json_ExplainRrfContributions_IncludeSourceAndRank()
    {
        var result = MakeSearchResultWithExplain();
        var json = SearchResultFormatter.ToJson([result], new FormatOptions { Query = "test", Explain = true });
        var arr = JsonSerializer.Deserialize<JsonElement>(json);
        var contribs = arr[0].GetProperty("explain").GetProperty("rrf").GetProperty("contributions");
        var first = contribs[0];
        first.GetProperty("source").GetString().Should().Be("fts");
        first.GetProperty("queryType").GetString().Should().Be("original");
        first.GetProperty("rank").GetInt32().Should().Be(2);
    }
}
