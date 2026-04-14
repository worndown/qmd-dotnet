using Qmd.Core.Models;

namespace Qmd.Core.Search;

internal static class RrfFusion
{
    public const int DefaultK = 60;
    public const double TopRankBonusFirst = 0.05;
    public const double TopRankBonusTop3 = 0.02;

    /// <summary>
    /// Reciprocal Rank Fusion: combine multiple ranked lists into a single ranking.
    /// Score per entry: weight / (k + rank + 1) where rank is 0-indexed.
    /// </summary>
    public static List<RankedResult> Fuse(
        List<List<RankedResult>> resultLists,
        List<double>? weights = null,
        int k = DefaultK)
    {
        var scores = new Dictionary<string, (RankedResult Result, double RrfScore, int TopRank)>();

        for (int listIdx = 0; listIdx < resultLists.Count; listIdx++)
        {
            var list = resultLists[listIdx];
            var weight = (weights != null && listIdx < weights.Count) ? weights[listIdx] : 1.0;

            for (int rank = 0; rank < list.Count; rank++)
            {
                var result = list[rank];
                var rrfContribution = weight / (k + rank + 1);

                if (scores.TryGetValue(result.File, out var existing))
                {
                    scores[result.File] = (existing.Result, existing.RrfScore + rrfContribution,
                        Math.Min(existing.TopRank, rank));
                }
                else
                {
                    scores[result.File] = (result, rrfContribution, rank);
                }
            }
        }

        // Top-rank bonus
        var entries = scores.Values.ToList();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            double bonus = e.TopRank == 0 ? TopRankBonusFirst : e.TopRank <= 2 ? TopRankBonusTop3 : 0;
            entries[i] = (e.Result, e.RrfScore + bonus, e.TopRank);
        }

        return entries
            .OrderByDescending(e => e.RrfScore)
            .Select(e => e.Result with { Score = e.RrfScore })
            .ToList();
    }

    /// <summary>
    /// Build per-document RRF contribution traces for explain output.
    /// Uses 1-indexed ranks in the trace.
    /// </summary>
    public static Dictionary<string, RrfScoreTrace> BuildTrace(
        List<List<RankedResult>> resultLists,
        List<double>? weights = null,
        List<RankedListMeta>? meta = null,
        int k = DefaultK)
    {
        var traces = new Dictionary<string, RrfScoreTrace>();

        for (int listIdx = 0; listIdx < resultLists.Count; listIdx++)
        {
            var list = resultLists[listIdx];
            var weight = (weights != null && listIdx < weights.Count) ? weights[listIdx] : 1.0;
            var listMeta = (meta != null && listIdx < meta.Count) ? meta[listIdx]
                : new RankedListMeta("fts", "original", "");

            for (int rank0 = 0; rank0 < list.Count; rank0++)
            {
                var result = list[rank0];
                var rank = rank0 + 1; // 1-indexed
                var contribution = weight / (k + rank);

                var detail = new RrfContributionTrace(
                    listIdx, listMeta.Source, listMeta.QueryType, listMeta.Query,
                    rank, weight, result.Score, contribution);

                if (traces.TryGetValue(result.File, out var existing))
                {
                    existing.BaseScore += contribution;
                    existing.TopRank = Math.Min(existing.TopRank, rank);
                    existing.Contributions.Add(detail);
                }
                else
                {
                    traces[result.File] = new RrfScoreTrace
                    {
                        Contributions = [detail],
                        BaseScore = contribution,
                        TopRank = rank,
                    };
                }
            }
        }

        foreach (var trace in traces.Values)
        {
            trace.TopRankBonus = trace.TopRank == 1 ? 0.05 : trace.TopRank <= 3 ? 0.02 : 0;
            trace.TotalScore = trace.BaseScore + trace.TopRankBonus;
        }

        return traces;
    }
}
