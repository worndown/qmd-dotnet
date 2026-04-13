using FluentAssertions;
using Qmd.Core.Snippets;

namespace Qmd.Core.Tests.Snippets;

public class SnippetExtractorTests
{
    private const string TestBody = @"Line one about introduction.
Line two about API reference.
Line three about configuration.
Line four about deployment.
Line five about monitoring.
Line six about troubleshooting.";

    [Fact]
    public void ExtractSnippet_FindsBestLine()
    {
        var result = SnippetExtractor.ExtractSnippet(TestBody, "API reference");
        result.Line.Should().Be(2); // "API reference" is on line 2
        result.Snippet.Should().Contain("API reference");
    }

    [Fact]
    public void ExtractSnippet_ReturnsContextWindow()
    {
        var result = SnippetExtractor.ExtractSnippet(TestBody, "configuration");
        result.SnippetLines.Should().BeGreaterThanOrEqualTo(2);
        result.SnippetLines.Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public void ExtractSnippet_HasDiffStyleHeader()
    {
        var result = SnippetExtractor.ExtractSnippet(TestBody, "API");
        result.Snippet.Should().StartWith("@@ -");
        result.Snippet.Should().Contain("before");
        result.Snippet.Should().Contain("after");
    }

    [Fact]
    public void ExtractSnippet_RespectsMaxLen()
    {
        var longBody = string.Join('\n', Enumerable.Range(0, 100).Select(i => $"Line {i} with some content about testing and APIs."));
        var result = SnippetExtractor.ExtractSnippet(longBody, "testing", maxLen: 100);
        // The snippet text (after header) should be truncated
        result.Snippet.Length.Should().BeLessThan(200); // header + truncated body
    }

    [Fact]
    public void ExtractSnippet_IntentWeighting()
    {
        // Body where "deployment" appears on a line without query terms,
        // but intent should boost it
        var body = "Line about cats.\nLine about deployment strategies.\nLine about dogs.";
        var result = SnippetExtractor.ExtractSnippet(body, "strategies", intent: "deployment infrastructure");
        // "deployment strategies" line should win (query term + intent term)
        result.Line.Should().Be(2);
    }

    [Fact]
    public void ExtractSnippet_ChunkScoped()
    {
        // Large body where match is at known position
        var prefix = string.Join('\n', Enumerable.Range(0, 50).Select(i => $"Filler line {i}."));
        var target = "\nThis line has the search target keyword.\n";
        var suffix = string.Join('\n', Enumerable.Range(0, 50).Select(i => $"More filler {i}."));
        var body = prefix + target + suffix;

        var chunkPos = prefix.Length;
        var result = SnippetExtractor.ExtractSnippet(body, "target", chunkPos: chunkPos);
        result.Snippet.Should().Contain("target");
    }

    [Fact]
    public void ExtractSnippet_TracksLineNumbers()
    {
        var result = SnippetExtractor.ExtractSnippet(TestBody, "monitoring");
        result.Line.Should().Be(5);
        result.LinesBefore.Should().BeGreaterThanOrEqualTo(0);
        result.LinesAfter.Should().BeGreaterThanOrEqualTo(0);
        (result.LinesBefore + result.SnippetLines + result.LinesAfter)
            .Should().Be(TestBody.Split('\n').Length);
    }

    private const string DisambiguationBody =
        "# Notes on Various Topics\n" +
        "\n" +
        "## Web Performance Section\n" +
        "Web performance means optimizing page load times and Core Web Vitals.\n" +
        "Reduce latency, improve rendering speed, and measure performance budgets.\n" +
        "\n" +
        "## Team Performance Section\n" +
        "Team performance depends on trust, psychological safety, and feedback.\n" +
        "Build culture where performance reviews drive growth not fear.\n" +
        "\n" +
        "## Health Performance Section\n" +
        "Health performance comes from consistent exercise, sleep, and endurance.\n" +
        "Track fitness metrics, optimize recovery, and monitor healthspan.";

    [Fact]
    public void ExtractSnippet_WithoutIntent_AnchorsOnQueryTermsOnly()
    {
        // "performance" appears in title and multiple sections — anchors on first match
        var result = SnippetExtractor.ExtractSnippet(DisambiguationBody, "performance", 500);
        result.Snippet.Should().Contain("Performance");
    }

    [Fact]
    public void ExtractSnippet_WithWebPerfIntent_PrefersWebPerformanceSection()
    {
        var result = SnippetExtractor.ExtractSnippet(
            DisambiguationBody, "performance", 500,
            null, null,
            "Looking for notes about web performance, latency, and page load times");
        result.Snippet.Should().MatchRegex("latency|page.*load|Core Web Vitals");
    }

    [Fact]
    public void ExtractSnippet_WithHealthIntent_PrefersHealthSection()
    {
        var result = SnippetExtractor.ExtractSnippet(
            DisambiguationBody, "performance", 500,
            null, null,
            "Looking for notes about personal health, fitness, and endurance");
        result.Snippet.Should().MatchRegex("health|fitness|endurance|exercise");
    }

    [Fact]
    public void ExtractSnippet_WithTeamIntent_PrefersTeamSection()
    {
        var result = SnippetExtractor.ExtractSnippet(
            DisambiguationBody, "performance", 500,
            null, null,
            "Looking for notes about building high-performing teams and culture");
        result.Snippet.Should().MatchRegex("team|culture|trust|feedback");
    }

    [Fact]
    public void ExtractSnippet_IntentDoesNotOverrideStrongQueryMatch()
    {
        // Query "Core Web Vitals" is very specific — intent should not override the strong query match
        var result = SnippetExtractor.ExtractSnippet(
            DisambiguationBody, "Core Web Vitals", 500,
            null, null,
            "Looking for notes about health and fitness");
        result.Snippet.Should().Contain("Core Web Vitals");
    }

    [Fact]
    public void ExtractSnippet_AbsentIntentSameAsUndefined()
    {
        var withoutIntent = SnippetExtractor.ExtractSnippet(DisambiguationBody, "performance", 500);
        var withNull = SnippetExtractor.ExtractSnippet(DisambiguationBody, "performance", 500, null, null, null);
        withoutIntent.Line.Should().Be(withNull.Line);
        withoutIntent.Snippet.Should().Be(withNull.Snippet);
    }

    [Fact]
    public void ExtractSnippet_IntentWithNoMatchingTermsFallsBackToQueryOnly()
    {
        var result = SnippetExtractor.ExtractSnippet(
            DisambiguationBody, "performance", 500,
            null, null,
            "quantum computing and entanglement");
        result.Snippet.Should().Contain("Performance");
        result.Snippet.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ExtractSnippet_IntentBreaksTieWhenQueryMatchesAllLinesEqually()
    {
        // Document where query term appears on every line but intent terms differ
        var body = "performance metrics for team velocity\n" +
                   "performance metrics for web latency\n" +
                   "performance metrics for athletic endurance";

        var noIntent = SnippetExtractor.ExtractSnippet(body, "performance metrics", 500);
        // Without intent, first line wins (all equal score)
        noIntent.Line.Should().Be(1);

        var withIntent = SnippetExtractor.ExtractSnippet(
            body, "performance metrics", 500,
            null, null,
            "web latency and page speed");
        // Intent terms "web", "latency" match line 2
        withIntent.Snippet.Should().Contain("web latency");
    }

    [Fact]
    public void ExtractSnippet_ReturnsBeginningWhenNoMatch()
    {
        var body = "First line\nSecond line\nThird line";
        var result = SnippetExtractor.ExtractSnippet(body, "nonexistent", 500);
        result.Line.Should().Be(1);
        result.Snippet.Should().Contain("First line");
    }

    [Fact]
    public void ExtractSnippet_LinesBeforeLinesAfterCorrect()
    {
        var body = "L1\nL2\nL3\nL4 match\nL5\nL6\nL7\nL8\nL9\nL10";
        var result = SnippetExtractor.ExtractSnippet(body, "match", 500);
        result.Line.Should().Be(4); // "L4 match" is line 4
        result.LinesBefore.Should().Be(2); // L1, L2 before snippet (snippet starts at L3)
        result.SnippetLines.Should().Be(4); // L3, L4, L5, L6
        result.LinesAfter.Should().Be(4); // L7, L8, L9, L10 after snippet
    }

    [Fact]
    public void ExtractSnippet_AtDocumentStartShows0Before()
    {
        var body = "First line keyword\nSecond\nThird\nFourth\nFifth";
        var result = SnippetExtractor.ExtractSnippet(body, "keyword", 500);
        result.Line.Should().Be(1);
        result.LinesBefore.Should().Be(0);
        result.SnippetLines.Should().Be(3); // First, Second, Third
        result.LinesAfter.Should().Be(2); // Fourth, Fifth
    }

    [Fact]
    public void ExtractSnippet_AtDocumentEndShows0After()
    {
        var body = "First\nSecond\nThird\nFourth\nFifth keyword";
        var result = SnippetExtractor.ExtractSnippet(body, "keyword", 500);
        result.Line.Should().Be(5);
        result.LinesBefore.Should().Be(3); // First, Second, Third
        result.SnippetLines.Should().Be(2); // Fourth, Fifth keyword
        result.LinesAfter.Should().Be(0);
    }

    [Fact]
    public void ExtractSnippet_SingleLineDocument()
    {
        var body = "Single line with keyword";
        var result = SnippetExtractor.ExtractSnippet(body, "keyword", 500);
        result.LinesBefore.Should().Be(0);
        result.LinesAfter.Should().Be(0);
        result.SnippetLines.Should().Be(1);
        result.Snippet.Should().Contain("@@ -1,1 @@ (0 before, 0 after)");
        result.Snippet.Should().Contain("Single line with keyword");
    }

    [Fact]
    public void IntentWeightSnippet_Is_0_3()
    {
        SnippetExtractor.IntentWeightSnippet.Should().Be(0.3);
    }

    [Fact]
    public void IntentWeightChunk_Is_0_5()
    {
        SnippetExtractor.IntentWeightChunk.Should().Be(0.5);
    }
}
