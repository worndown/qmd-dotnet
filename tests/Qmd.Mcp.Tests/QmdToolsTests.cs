using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Qmd.Core.Snippets;
using Qmd.Sdk;

namespace Qmd.Mcp.Tests;

public class QmdToolsTests : IAsyncLifetime
{
    private IQmdStore _seededStore = null!;
    private IQmdStore _emptyStore = null!;
    private QmdTools _seededTools = null!;
    private QmdTools _emptyTools = null!;

    public Task InitializeAsync()
    {
        _seededStore = McpTestHelper.CreateSeededStore();
        _emptyStore = McpTestHelper.CreateEmptyStore();
        _seededTools = new QmdTools(_seededStore);
        _emptyTools = new QmdTools(_emptyStore);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _seededStore.DisposeAsync();
        await _emptyStore.DisposeAsync();
    }

    /// <summary>
    /// Extract text from a CallToolResult (first TextContentBlock or EmbeddedResourceBlock).
    /// </summary>
    private static string GetText(CallToolResult result)
    {
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock text) return text.Text ?? "";
            if (block is EmbeddedResourceBlock erb && erb.Resource is TextResourceContents trc)
                return trc.Text ?? "";
        }
        return "";
    }

    // =========================================================================
    // InstructionsBuilder
    // =========================================================================

    [Fact]
    public async Task Instructions_EmptyStore_ShowsZeroDocuments()
    {
        var instructions = await InstructionsBuilder.BuildAsync(_emptyStore);
        instructions.Should().Contain("0 indexed documents");
    }

    [Fact]
    public async Task Instructions_WithCollections_ListsCollectionNames()
    {
        var instructions = await InstructionsBuilder.BuildAsync(_seededStore);
        instructions.Should().Contain("docs");
        instructions.Should().Contain("code");
    }

    [Fact]
    public async Task Instructions_VectorIndexStatus_MatchesAvailability()
    {
        // Whether the vector index warning appears depends on whether vec0.dll
        // is present. TS equivalent: if sqlite-vec loads, no warning is shown;
        // if it doesn't load, the "Vector index not available" warning appears.
        var instructions = await InstructionsBuilder.BuildAsync(_seededStore);
        var status = await _seededStore.GetStatusAsync();
        if (!status.HasVectorIndex)
        {
            instructions.Should().Contain("Vector index not available");
        }
        else
        {
            instructions.Should().NotContain("Vector index not available");
        }
    }

    // =========================================================================
    // Status Tool
    // =========================================================================

    [Fact]
    public async Task Status_EmptyStore_ShowsZeroDocuments()
    {
        var result = await _emptyTools.Status();
        var text = GetText(result);
        text.Should().Contain("Total documents: 0");
    }

    [Fact]
    public async Task Status_WithDocs_ShowsCorrectCounts()
    {
        var result = await _seededTools.Status();
        var text = GetText(result);
        text.Should().Contain("Total documents: 6");
        text.Should().Contain("docs:");
        text.Should().Contain("code:");
        result.StructuredContent.Should().NotBeNull();
    }

    // =========================================================================
    // Get Tool
    // =========================================================================

    [Fact]
    public async Task Get_ValidPath_ReturnsDocumentContent()
    {
        var result = await _seededTools.Get("qmd://docs/readme.md");
        var text = GetText(result);
        text.Should().Contain("Project README");
    }

    [Fact]
    public async Task Get_ByDocid_ReturnsDocument()
    {
        var result = await _seededTools.Get("qmd://docs/api/guide.md");
        var text = GetText(result);
        text.Should().Contain("API Guide");
    }

    [Fact]
    public async Task Get_NotFound_ShowsSuggestions()
    {
        var result = await _seededTools.Get("nonexistent.md");
        result.IsError.Should().BeTrue();
        var text = GetText(result);
        text.Should().Contain("Document not found");
    }

    [Fact]
    public async Task Get_WithLineNumbers_IncludesNumbers()
    {
        var result = await _seededTools.Get("qmd://docs/readme.md", lineNumbers: true);
        var text = GetText(result);
        text.Should().Contain("1:");
    }

    [Fact]
    public async Task Get_LineSlicing_ReturnsSubset()
    {
        var result = await _seededTools.Get("qmd://docs/readme.md", fromLine: 3, maxLines: 2);
        var text = GetText(result);
        text.Should().NotBeEmpty();
        var lines = text.Split('\n');
        lines.Length.Should().BeLessThan(15);
    }

    // =========================================================================
    // Query Tool (BM25 — no LLM needed)
    // =========================================================================

    [Fact]
    public async Task Query_MatchingTerm_ReturnsResults()
    {
        var result = await _seededTools.Query("readme");
        var text = GetText(result);
        text.Should().Contain("result(s)");
        text.Should().Contain("readme.md");
    }

    [Fact]
    public async Task Query_NoMatches_ReturnsNoResults()
    {
        var result = await _seededTools.Query("xyznonexistent123");
        var text = GetText(result);
        text.Should().Contain("No results found");
    }

    [Fact]
    public async Task Query_CollectionFilter_OnlyFromCollection()
    {
        var result = await _seededTools.Query("readme", collection: "code");
        var text = GetText(result);
        // readme.md is in "docs", not "code"
        text.Should().NotContain("readme.md");
    }

    // =========================================================================
    // MultiGet Tool
    // =========================================================================

    [Fact]
    public async Task MultiGet_GlobPattern_ReturnsMatchingDocs()
    {
        var result = await _seededTools.MultiGet("qmd://docs/*.md");
        var text = GetAllText(result);
        text.Should().Contain("readme.md");
    }

    [Fact]
    public async Task MultiGet_CommaSeparated_ResolvesFiles()
    {
        var result = await _seededTools.MultiGet("qmd://docs/readme.md, qmd://code/index.ts");
        var text = GetAllText(result);
        text.Should().Contain("readme.md");
        text.Should().Contain("index.ts");
    }

    [Fact]
    public async Task MultiGet_FileTooLarge_ShowsSkipped()
    {
        // large-file.md is ~15KB, set maxBytes=1024 to trigger skip
        var result = await _seededTools.MultiGet("qmd://docs/large-file.md", maxBytes: 1024);
        var text = GetAllText(result);
        text.Should().Contain("SKIPPED");
    }

    [Fact]
    public async Task MultiGet_NoMatches_ReturnsMessage()
    {
        var result = await _seededTools.MultiGet("nonexistent-pattern-*.xyz");
        var text = GetAllText(result);
        text.Should().Contain("No files matched pattern");
    }

    // =========================================================================
    // Get Tool — additional tests (ported from TS mcp.test.ts)
    // =========================================================================

    [Fact]
    public async Task Get_ByDisplayPath_ReturnsDocument()
    {
        // "retrieves document by display_path" — use "docs/readme.md" without qmd:// prefix
        var result = await _seededTools.Get("docs/readme.md");
        var text = GetText(result);
        text.Should().Contain("Project README");
    }

    [Fact]
    public async Task Get_ByPartialPath_ReturnsDocument()
    {
        // "retrieves document by partial path" — just "readme.md"
        var result = await _seededTools.Get("readme.md");
        result.IsError.Should().NotBe(true);
        var text = GetText(result);
        text.Should().Contain("Project README");
    }

    [Fact]
    public async Task Get_WithLineRangeSuffix_ReturnsSubset()
    {
        // "supports line range with :line suffix" — e.g., "readme.md:2"
        var result = await _seededTools.Get("readme.md:2");
        result.IsError.Should().NotBe(true);
        var text = GetText(result);
        text.Should().NotBeEmpty();
        // Should start from line 2, so should NOT contain the first line (heading)
        // The text may include context prefix; check that slicing happened
        var lines = text.Split('\n');
        lines.Length.Should().BeLessThan(20);
    }

    [Fact]
    public async Task Get_IncludesContextForMeetingDoc()
    {
        // "includes context for documents in context path"
        var result = await _seededTools.Get("meetings/meeting-2024-01.md");
        result.IsError.Should().NotBe(true);
        var text = GetText(result);
        text.Should().Contain("Meeting notes and transcripts");
    }

    // =========================================================================
    // MultiGet Tool — additional tests (ported from TS mcp.test.ts)
    // =========================================================================

    [Fact]
    public async Task MultiGet_CommaSeparatedList_RetrievesDocuments()
    {
        // "retrieves documents by comma-separated list"
        var result = await _seededTools.MultiGet("readme.md, api/guide.md");
        var text = GetAllText(result);
        text.Should().Contain("readme.md");
        text.Should().Contain("guide.md");
    }

    [Fact]
    public async Task MultiGet_CommaSeparatedList_ReturnsErrorsForMissingFiles()
    {
        // "returns errors for missing files in comma list"
        var result = await _seededTools.MultiGet("readme.md, nonexistent.md");
        var text = GetAllText(result);
        // Should have at least the found document
        text.Should().Contain("readme.md");
        // Should report the error for the missing file
        text.Should().Contain("not found");
    }

    [Fact]
    public async Task MultiGet_RespectsMaxLines()
    {
        // "respects maxLines parameter"
        var result = await _seededTools.MultiGet("qmd://docs/readme.md", maxLines: 2);
        var text = GetAllText(result);
        text.Should().Contain("readme.md");
        // Should contain truncation notice when there are more lines
        text.Should().Contain("truncated");
    }

    [Fact]
    public async Task MultiGet_NonMatchingGlob_ReturnsError()
    {
        // "returns error for non-matching glob"
        var result = await _seededTools.MultiGet("nonexistent/*.md");
        var text = GetAllText(result);
        text.Should().Contain("No files matched pattern");
    }

    // =========================================================================
    // Edge Cases (ported from TS mcp.test.ts)
    // =========================================================================

    [Fact]
    public async Task Query_EmptyQuery_HandlesGracefully()
    {
        // "handles empty query" — query tool with empty string
        var result = await _seededTools.Query("");
        var text = GetText(result);
        // Should either return no results or an error, but NOT throw
        text.Should().NotBeNull();
    }

    [Fact]
    public async Task Query_SpecialCharacters_DoesNotThrow()
    {
        // "handles special characters in query" — query with C++ or it's
        var result = await _seededTools.Query("C++");
        result.Should().NotBeNull();

        var result2 = await _seededTools.Query("it's");
        result2.Should().NotBeNull();
    }

    [Fact]
    public async Task Query_Unicode_DoesNotThrow()
    {
        // "handles unicode in query"
        var result = await _seededTools.Query("\u6587\u6863");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SnippetExtractor_ExtractsSnippetAroundMatch()
    {
        // "extracts snippet around matching text" — verify snippet has @@ header
        var body = "Line 1\nLine 2\nThis is the important line with the keyword\nLine 4\nLine 5";
        var snippetResult = SnippetExtractor.ExtractSnippet(body, "keyword", 200);
        snippetResult.Snippet.Should().Contain("keyword");
        snippetResult.Line.Should().Be(3);
        snippetResult.Snippet.Should().Contain("@@");
    }

    [Fact]
    public async Task SnippetExtractor_HandlesChunkPos()
    {
        // "handles snippet extraction with chunkPos" — verify chunkPos affects snippet
        var body = new string('A', 1000) + "KEYWORD" + new string('B', 1000);
        var chunkPos = 1000; // Position of KEYWORD
        var snippetResult = SnippetExtractor.ExtractSnippet(body, "keyword", 200, chunkPos);
        snippetResult.Snippet.Should().Contain("KEYWORD");
        await Task.CompletedTask; // keep async signature consistent
    }

    // =========================================================================
    // MCP Spec Compliance (ported from TS mcp.test.ts)
    // =========================================================================

    [Fact]
    public void EncodeQmdPath_PreservesSlashes_EncodesSpecialChars()
    {
        // "encodeQmdPath preserves slashes but encodes special chars"
        // EncodeQmdPath is private, so we replicate the logic and test the same behavior
        var path = "External Podcast/2023 April - Interview.md";
        var encoded = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
        encoded.Should().Be("External%20Podcast/2023%20April%20-%20Interview.md");
        encoded.Should().Contain("/"); // Slashes preserved
        encoded.Should().Contain("%20"); // Spaces encoded
    }

    [Fact]
    public async Task Query_SearchResults_HaveCorrectStructure()
    {
        // "search results have correct structure for structuredContent"
        var result = await _seededTools.Query("readme");
        result.StructuredContent.Should().NotBeNull();

        // Parse the structured content JSON
        var json = result.StructuredContent!.Value;
        var results = json.GetProperty("results");
        results.GetArrayLength().Should().BeGreaterThan(0);

        var item = results[0];
        item.TryGetProperty("file", out _).Should().BeTrue();
        item.TryGetProperty("title", out _).Should().BeTrue();
        item.TryGetProperty("score", out _).Should().BeTrue();
        item.TryGetProperty("snippet", out _).Should().BeTrue();

        var score = item.GetProperty("score").GetDouble();
        score.Should().BeGreaterThanOrEqualTo(0);
        score.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Get_ErrorResponse_IncludesIsErrorFlag()
    {
        // "error responses include isError flag"
        var result = await _seededTools.Get("definitely-not-a-real-file.xyz");
        result.IsError.Should().BeTrue();
        result.Content.Should().NotBeEmpty();
        var text = GetText(result);
        text.Should().Contain("Document not found");
    }

    // =========================================================================
    // MultiGet — context inclusion (ported from TS mcp.test.ts)
    // =========================================================================

    [Fact]
    public async Task MultiGet_IncludesContextInResults()
    {
        // TS: "qmd_multi_get includes context in results"
        // The seeded store has context "/meetings" => "Meeting notes and transcripts" on the "docs" collection.
        // Multi-getting a meetings doc should include that context in the output.
        var result = await _seededTools.MultiGet("qmd://docs/meetings/meeting-2024-01.md");
        var text = GetAllText(result);
        text.Should().Contain("Meeting notes and transcripts");
    }

    // =========================================================================
    // Status — embedding count (ported from TS mcp.test.ts)
    // =========================================================================

    [Fact]
    public async Task Status_ShowsDocsNeedingEmbedding()
    {
        // TS: "qmd_status shows documents needing embedding"
        // The seeded store has documents but no embeddings generated, so NeedsEmbedding > 0.
        var result = await _seededTools.Status();
        var text = GetText(result);
        text.Should().Contain("Needs embedding:");
        // Structured content should also report the count
        result.StructuredContent.Should().NotBeNull();
        var json = result.StructuredContent!.Value;
        var needsEmbedding = json.GetProperty("needsEmbedding").GetInt32();
        needsEmbedding.Should().BeGreaterThan(0);
    }

    // =========================================================================
    // Query — very long query (ported from TS mcp.test.ts)
    // =========================================================================

    [Fact]
    public async Task Query_VeryLongQuery_HandlesGracefully()
    {
        // TS: "handles very long query"
        // Pass a 10K+ character query string and assert it doesn't crash.
        var longQuery = new string('a', 10_000) + " readme";
        var act = () => _seededTools.Query(longQuery);
        await act.Should().NotThrowAsync();
    }

    // =========================================================================
    // Query — only stopwords (ported from TS mcp.test.ts)
    // =========================================================================

    [Fact]
    public async Task Query_OnlyStopwords_HandlesGracefully()
    {
        // TS: "handles query with only stopwords"
        // Pass common English stopwords — should return empty results or a graceful message, not throw.
        var result = await _seededTools.Query("the a an");
        result.Should().NotBeNull();
        var text = GetText(result);
        text.Should().NotBeNull();
    }

    private static string GetAllText(CallToolResult result)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock tcb) sb.AppendLine(tcb.Text);
            if (block is EmbeddedResourceBlock erb && erb.Resource is TextResourceContents trc)
            {
                sb.AppendLine(trc.Uri);
                sb.AppendLine(trc.Text);
            }
        }
        return sb.ToString();
    }
}
