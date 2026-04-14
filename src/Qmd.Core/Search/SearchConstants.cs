namespace Qmd.Core.Search;

public static class SearchConstants
{
    public const double StrongSignalMinScore = 0.85;
    public const double StrongSignalMinGap = 0.15;
    public const int RerankCandidateLimit = 40;

    /// <summary>
    /// Pre-reranking gate: when BM25 returns nothing, discard vector results
    /// below this cosine similarity threshold before RRF fusion.
    /// </summary>
    public const double VecOnlyGateThreshold = 0.55;

    /// <summary>
    /// Reranker gate: when the best reranker score (Qwen3-Reranker, [0-1]) is
    /// below this value, treat the entire result set as irrelevant.
    /// </summary>
    public const double RerankGateThreshold = 0.1;

    /// <summary>
    /// Post-fusion confidence gap: drop results scoring below this fraction
    /// of the top result's blended score.
    /// </summary>
    public const double ConfidenceGapRatio = 0.5;
}
