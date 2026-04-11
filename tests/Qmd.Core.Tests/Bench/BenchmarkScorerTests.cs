using FluentAssertions;
using Qmd.Core.Bench;

namespace Qmd.Core.Tests.Bench;

/// <summary>
/// Tests for the benchmark scoring functions.
/// </summary>
public class BenchmarkScorerTests
{
    // =========================================================================
    // NormalizePath
    // =========================================================================

    [Fact]
    public void NormalizePath_Lowercases()
    {
        BenchmarkScorer.NormalizePath("Resources/Concepts/Context Engineering.md")
            .Should().Be("resources/concepts/context engineering.md");
    }

    [Fact]
    public void NormalizePath_StripsQmdPrefix()
    {
        BenchmarkScorer.NormalizePath("qmd://collection/docs/readme.md")
            .Should().Be("docs/readme.md");
    }

    [Fact]
    public void NormalizePath_StripsSlashes()
    {
        BenchmarkScorer.NormalizePath("/docs/readme.md/")
            .Should().Be("docs/readme.md");
    }

    [Fact]
    public void NormalizePath_HandlesPlainFilename()
    {
        BenchmarkScorer.NormalizePath("readme.md")
            .Should().Be("readme.md");
    }

    // =========================================================================
    // PathsMatch
    // =========================================================================

    [Fact]
    public void PathsMatch_ExactMatch()
    {
        BenchmarkScorer.PathsMatch("docs/readme.md", "docs/readme.md")
            .Should().BeTrue();
    }

    [Fact]
    public void PathsMatch_CaseInsensitive()
    {
        BenchmarkScorer.PathsMatch("Docs/README.md", "docs/readme.md")
            .Should().BeTrue();
    }

    [Fact]
    public void PathsMatch_SuffixMatch_ResultIsLonger()
    {
        BenchmarkScorer.PathsMatch("/full/path/docs/readme.md", "docs/readme.md")
            .Should().BeTrue();
    }

    [Fact]
    public void PathsMatch_SuffixMatch_ExpectedIsLonger()
    {
        BenchmarkScorer.PathsMatch("readme.md", "docs/readme.md")
            .Should().BeTrue();
    }

    [Fact]
    public void PathsMatch_QmdPrefixHandled()
    {
        BenchmarkScorer.PathsMatch("qmd://col/docs/readme.md", "docs/readme.md")
            .Should().BeTrue();
    }

    [Fact]
    public void PathsMatch_DifferentFiles_ReturnsFalse()
    {
        BenchmarkScorer.PathsMatch("docs/readme.md", "docs/other.md")
            .Should().BeFalse();
    }

    // =========================================================================
    // ScoreResults
    // =========================================================================

    [Fact]
    public void ScoreResults_PerfectScore_AllExpectedInTopK()
    {
        var result = BenchmarkScorer.ScoreResults(
            ["a.md", "b.md", "c.md"],
            ["a.md", "b.md"],
            topK: 2);

        result.PrecisionAtK.Should().Be(1);
        result.Recall.Should().Be(1);
        result.Mrr.Should().Be(1);
        result.F1.Should().Be(1);
        result.HitsAtK.Should().Be(2);
    }

    [Fact]
    public void ScoreResults_ZeroScore_NoneFound()
    {
        var result = BenchmarkScorer.ScoreResults(
            ["x.md", "y.md", "z.md"],
            ["a.md", "b.md"],
            topK: 2);

        result.PrecisionAtK.Should().Be(0);
        result.Recall.Should().Be(0);
        result.Mrr.Should().Be(0);
        result.F1.Should().Be(0);
        result.HitsAtK.Should().Be(0);
    }

    [Fact]
    public void ScoreResults_Partial_FoundOutsideTopK()
    {
        var result = BenchmarkScorer.ScoreResults(
            ["x.md", "y.md", "a.md"],
            ["a.md"],
            topK: 1);

        result.PrecisionAtK.Should().Be(0); // not in top-1
        result.Recall.Should().Be(1); // found somewhere
        result.Mrr.Should().BeApproximately(1.0 / 3, 0.001); // rank 3
        result.HitsAtK.Should().Be(0);
    }

    [Fact]
    public void ScoreResults_Mrr_FirstRelevantAtRank2()
    {
        var result = BenchmarkScorer.ScoreResults(
            ["x.md", "a.md", "b.md"],
            ["a.md", "b.md"],
            topK: 3);

        result.Mrr.Should().BeApproximately(0.5, 0.001); // 1/2
    }

    [Fact]
    public void ScoreResults_EmptyResults()
    {
        var result = BenchmarkScorer.ScoreResults(
            [],
            ["a.md"],
            topK: 1);

        result.PrecisionAtK.Should().Be(0);
        result.Recall.Should().Be(0);
        result.Mrr.Should().Be(0);
    }

    [Fact]
    public void ScoreResults_EmptyExpected()
    {
        var result = BenchmarkScorer.ScoreResults(
            ["a.md"],
            [],
            topK: 1);

        result.PrecisionAtK.Should().Be(0);
        result.Recall.Should().Be(0);
    }
}
