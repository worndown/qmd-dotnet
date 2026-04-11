using FluentAssertions;
using Qmd.Core.Search;
using Qmd.Core.Snippets;

namespace Qmd.Core.Tests.Snippets;

public class IntentProcessorTests
{
    [Fact]
    public void ExtractIntentTerms_FiltersStopWords()
    {
        var terms = IntentProcessor.ExtractIntentTerms("find the API documentation for authentication");
        terms.Should().Contain("api");
        terms.Should().Contain("documentation");
        terms.Should().Contain("authentication");
        terms.Should().NotContain("find");
        terms.Should().NotContain("the");
        terms.Should().NotContain("for");
    }

    [Fact]
    public void ExtractIntentTerms_PreservesShortDomainTerms()
    {
        var terms = IntentProcessor.ExtractIntentTerms("SQL and API and CDN");
        terms.Should().Contain("sql");
        terms.Should().Contain("api");
        terms.Should().Contain("cdn");
        terms.Should().NotContain("and");
    }

    [Fact]
    public void ExtractIntentTerms_StripsPunctuation()
    {
        var terms = IntentProcessor.ExtractIntentTerms("(deployment) [strategies] config.");
        terms.Should().Contain("deployment");
        terms.Should().Contain("strategies");
        terms.Should().Contain("config");
    }

    [Fact]
    public void ExtractIntentTerms_Lowercases()
    {
        var terms = IntentProcessor.ExtractIntentTerms("API Documentation");
        terms.Should().AllSatisfy(t => t.Should().Be(t.ToLowerInvariant()));
    }

    [Fact]
    public void ExtractIntentTerms_FiltersSingleCharTerms()
    {
        var terms = IntentProcessor.ExtractIntentTerms("a b c database");
        terms.Should().HaveCount(1);
        terms.Should().Contain("database");
    }

    [Fact]
    public void ExtractIntentTerms_EmptyInput()
    {
        IntentProcessor.ExtractIntentTerms("").Should().BeEmpty();
        IntentProcessor.ExtractIntentTerms("the and for").Should().BeEmpty();
    }

    [Fact]
    public void ExtractIntentTerms_FiltersStopWords_TS()
    {
        // TS: "looking for notes about latency optimization" → ["latency", "optimization"]
        var terms = IntentProcessor.ExtractIntentTerms("looking for notes about latency optimization");
        terms.Should().Equal("latency", "optimization");
    }

    [Fact]
    public void ExtractIntentTerms_FiltersCommonFunctionWords()
    {
        // TS: "what is the best way to find" → ["best", "way"]
        var terms = IntentProcessor.ExtractIntentTerms("what is the best way to find");
        terms.Should().Equal("best", "way");
    }

    [Fact]
    public void ExtractIntentTerms_PreservesDomainTerms()
    {
        // TS: "web performance latency page load times" → all survive
        var terms = IntentProcessor.ExtractIntentTerms("web performance latency page load times");
        terms.Should().Equal("web", "performance", "latency", "page", "load", "times");
    }

    [Fact]
    public void ExtractIntentTerms_HandlesSurroundingPunctuationWithUnicodeAwareness()
    {
        // TS: "personal health, fitness, and endurance" → ["personal", "health", "fitness", "endurance"]
        var terms = IntentProcessor.ExtractIntentTerms("personal health, fitness, and endurance");
        terms.Should().Equal("personal", "health", "fitness", "endurance");
    }

    [Fact]
    public void ExtractIntentTerms_PreservesInternalHyphens()
    {
        // TS: "self-hosted real-time (decision-making)" → ["self-hosted", "real-time", "decision-making"]
        var terms = IntentProcessor.ExtractIntentTerms("self-hosted real-time (decision-making)");
        terms.Should().Equal("self-hosted", "real-time", "decision-making");
    }

    [Fact]
    public void ExtractIntentTerms_ShortDomainTermsSurvive_API_SQL_LLM()
    {
        // TS: "API design for LLM agents" → ["api", "design", "llm", "agents"]
        var terms = IntentProcessor.ExtractIntentTerms("API design for LLM agents");
        terms.Should().Equal("api", "design", "llm", "agents");
    }

    [Fact]
    public void ExtractIntentTerms_Preserves2CharDomainTerms_CI_CD_DB()
    {
        // TS: "SQL CI CD DB" → contains all four
        var terms = IntentProcessor.ExtractIntentTerms("SQL CI CD DB");
        terms.Should().Contain("sql");
        terms.Should().Contain("ci");
        terms.Should().Contain("cd");
        terms.Should().Contain("db");
    }

    [Fact]
    public void ExtractIntentTerms_LowercasesAllTerms()
    {
        // TS: "WebSocket HTTP REST" → ["websocket", "http", "rest"]
        var terms = IntentProcessor.ExtractIntentTerms("WebSocket HTTP REST");
        terms.Should().Contain("websocket");
        terms.Should().Contain("http");
        terms.Should().Contain("rest");
    }

    [Fact]
    public void ExtractIntentTerms_HandlesCppStylePunctuation()
    {
        // TS: "C++, performance! optimization." → contains "performance", "optimization"
        var terms = IntentProcessor.ExtractIntentTerms("C++, performance! optimization.");
        terms.Should().Contain("performance");
        terms.Should().Contain("optimization");
    }

    [Fact]
    public void ExtractIntentTerms_AllStopWordsReturnsEmpty()
    {
        // TS: "the and or but in on at to for of with by" → []
        var terms = IntentProcessor.ExtractIntentTerms("the and or but in on at to for of with by");
        terms.Should().BeEmpty();
    }

    [Fact]
    public void ExtractIntentTerms_ReturnsEmptyForWhitespace()
    {
        IntentProcessor.ExtractIntentTerms("  ").Should().BeEmpty();
    }

    // =========================================================================
    // Intent keyword extraction / chunk scoring logic (ported from TS)
    // =========================================================================

    private static readonly string[] Chunks =
    [
        "Web performance: optimize page load times, reduce latency, improve rendering pipeline.",
        "Team performance: build trust, give feedback, set clear expectations for the group.",
        "Health performance: exercise regularly, sleep 8 hours, manage stress for endurance.",
    ];

    private static double ScoreChunk(string text, string query, string? intent = null)
    {
        var queryTerms = query.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .ToArray();
        var intentTerms = intent != null ? IntentProcessor.ExtractIntentTerms(intent) : [];
        var lower = text.ToLowerInvariant();
        double qScore = queryTerms.Count(term => lower.Contains(term));
        double iScore = intentTerms.Count(term => lower.Contains(term)) * SnippetExtractor.IntentWeightChunk;
        return qScore + iScore;
    }

    [Fact]
    public void WithoutIntent_AllChunksScoreEquallyOnPerformance()
    {
        var scores = Chunks.Select(c => ScoreChunk(c, "performance")).ToArray();
        // All contain "performance", so all score 1
        scores[0].Should().Be(scores[1]);
        scores[1].Should().Be(scores[2]);
    }

    [Fact]
    public void WithWebIntent_WebChunkScoresHighest()
    {
        var intent = "looking for notes about page load times and latency optimization";
        var scores = Chunks.Select(c => ScoreChunk(c, "performance", intent)).ToArray();
        scores[0].Should().BeGreaterThan(scores[1]);
        scores[0].Should().BeGreaterThan(scores[2]);
    }

    [Fact]
    public void WithHealthIntent_HealthChunkScoresHighest()
    {
        var intent = "looking for notes about exercise, sleep, and endurance";
        var scores = Chunks.Select(c => ScoreChunk(c, "performance", intent)).ToArray();
        scores[2].Should().BeGreaterThan(scores[0]);
        scores[2].Should().BeGreaterThan(scores[1]);
    }

    [Fact]
    public void IntentTerms_HaveLowerWeightThanQueryTerms()
    {
        var intent = "looking for latency";
        // Chunk 0 has "performance" (query: 1.0) + "latency" (intent: IntentWeightChunk) = 1.5
        var withBoth = ScoreChunk(Chunks[0], "performance", intent);
        var queryOnly = ScoreChunk(Chunks[0], "performance");
        withBoth.Should().Be(queryOnly + SnippetExtractor.IntentWeightChunk);
    }

    [Fact]
    public void StopWordsFiltered_ShortDomainTermsSurvive()
    {
        var intent = "the art of web performance";
        // "the" (stop word), "art" (survives), "of" (stop word),
        // "web" (survives), "performance" (survives)
        // intent terms after filtering: ["art", "web", "performance"]
        // Chunk 0 has "web" + "performance" -> 2 intent hits (no "art")
        // Chunks 1,2 have "performance" only -> 1 intent hit
        var scores = Chunks.Select(c => ScoreChunk(c, "test", intent)).ToArray();
        scores[0].Should().Be(SnippetExtractor.IntentWeightChunk * 2); // "web" + "performance"
        scores[1].Should().Be(SnippetExtractor.IntentWeightChunk);      // "performance" only
        scores[2].Should().Be(SnippetExtractor.IntentWeightChunk);      // "performance" only
    }

    // =========================================================================
    // Strong-signal bypass logic
    // =========================================================================

    private static bool HasStrongSignal(double topScore, double secondScore, string? intent = null)
    {
        return intent == null
            && topScore >= SearchConstants.StrongSignalMinScore
            && (topScore - secondScore) >= SearchConstants.StrongSignalMinGap;
    }

    [Fact]
    public void StrongSignal_DetectedWithoutIntent()
    {
        HasStrongSignal(0.90, 0.70).Should().BeTrue();
    }

    [Fact]
    public void StrongSignal_BypassedWhenIntentProvided()
    {
        HasStrongSignal(0.90, 0.70, "looking for health performance").Should().BeFalse();
    }

    [Fact]
    public void WeakSignal_NotAffectedByIntent()
    {
        HasStrongSignal(0.50, 0.45).Should().BeFalse();
        HasStrongSignal(0.50, 0.45, "some intent").Should().BeFalse();
    }

    [Fact]
    public void CloseScores_NotStrongEvenWithoutIntent()
    {
        HasStrongSignal(0.90, 0.80).Should().BeFalse(); // gap < 0.15
    }
}
