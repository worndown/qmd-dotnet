using FluentAssertions;
using Qmd.Core.Models;
using Qmd.Core.Search;

namespace Qmd.Core.Tests.Search;

public class RrfFusionTests
{
    private static RankedResult R(string file, double score = 0) =>
        new(file, file, file, "", score, "hash");

    [Fact]
    public void Fuse_SingleList_RanksCorrectly()
    {
        var lists = new List<List<RankedResult>> { new() { R("a"), R("b"), R("c") } };
        var result = RrfFusion.Fuse(lists);
        result.Should().HaveCount(3);
        result[0].File.Should().Be("a"); // rank 0 highest
        result[0].Score.Should().BeGreaterThan(result[1].Score);
    }

    [Fact]
    public void Fuse_TwoLists_AggregatesScores()
    {
        var lists = new List<List<RankedResult>>
        {
            new() { R("a"), R("b") },
            new() { R("b"), R("a") },
        };
        var result = RrfFusion.Fuse(lists);
        // Both a and b appear in both lists, should have similar scores
        result.Should().HaveCount(2);
    }

    [Fact]
    public void Fuse_TopRankBonus_Rank0Gets005()
    {
        var lists = new List<List<RankedResult>> { new() { R("a") } };
        var result = RrfFusion.Fuse(lists);
        // Score = 1.0/(60+0+1) + 0.05 bonus = ~0.0164 + 0.05 = ~0.0664
        result[0].Score.Should().BeApproximately(1.0 / 61 + 0.05, 0.001);
    }

    [Fact]
    public void Fuse_TopRankBonus_Rank1Gets002()
    {
        // "b" only appears at rank 1 (0-indexed)
        var lists = new List<List<RankedResult>> { new() { R("a"), R("b") } };
        var result = RrfFusion.Fuse(lists);
        var bResult = result.Find(r => r.File == "b")!;
        bResult.Score.Should().BeApproximately(1.0 / 62 + 0.02, 0.001);
    }

    [Fact]
    public void Fuse_Weights_Respected()
    {
        var lists = new List<List<RankedResult>>
        {
            new() { R("a") },
            new() { R("b") },
        };
        var weights = new List<double> { 2.0, 1.0 };
        var result = RrfFusion.Fuse(lists, weights);
        // "a" has weight 2.0, "b" has weight 1.0
        result[0].File.Should().Be("a");
    }

    [Fact]
    public void Fuse_EmptyLists_ReturnsEmpty()
    {
        RrfFusion.Fuse([]).Should().BeEmpty();
    }

    [Fact]
    public void Fuse_K60_Default()
    {
        var lists = new List<List<RankedResult>> { new() { R("a") } };
        var result = RrfFusion.Fuse(lists, k: 60);
        // weight=1, rank=0: 1/(60+0+1) = 1/61 + 0.05 bonus
        result[0].Score.Should().BeApproximately(1.0 / 61 + 0.05, 0.0001);
    }

    [Fact]
    public void Fuse_DocumentInMultipleLists_Sums()
    {
        var lists = new List<List<RankedResult>>
        {
            new() { R("a"), R("b") },    // a at rank 0, b at rank 1
            new() { R("a") },             // a at rank 0 again
        };
        var result = RrfFusion.Fuse(lists);
        var aScore = result.Find(r => r.File == "a")!.Score;
        var bScore = result.Find(r => r.File == "b")!.Score;
        aScore.Should().BeGreaterThan(bScore); // a appears twice
    }

    [Fact]
    public void BuildTrace_TracksContributions()
    {
        var lists = new List<List<RankedResult>>
        {
            new() { R("a", 0.9) },
            new() { R("a", 0.8) },
        };
        var meta = new List<RankedListMeta>
        {
            new("fts", "lex", "query1"),
            new("vec", "vec", "query2"),
        };
        var traces = RrfFusion.BuildTrace(lists, meta: meta);
        traces.Should().ContainKey("a");
        traces["a"].Contributions.Should().HaveCount(2);
        traces["a"].TotalScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BuildTrace_TopRankBonus()
    {
        var lists = new List<List<RankedResult>> { new() { R("a") } };
        var traces = RrfFusion.BuildTrace(lists);
        traces["a"].TopRank.Should().Be(1); // 1-indexed in trace
        traces["a"].TopRankBonus.Should().Be(0.05);
    }
}
