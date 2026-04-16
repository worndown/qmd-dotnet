namespace Qmd.Core.Models;

// RRF types
public record RankedResult(string File, string DisplayPath, string Title, string Body, double Score, string Hash);

public record RankedListMeta(string Source, string QueryType, string Query);

public record RrfContributionTrace(int ListIndex, string Source, string QueryType, string Query,
    int Rank, double Weight, double BackendScore, double RrfContribution);

public class RrfScoreTrace
{
    public List<RrfContributionTrace> Contributions { get; set; } = [];
    public double BaseScore { get; set; }
    public int TopRank { get; set; }
    public double TopRankBonus { get; set; }
    public double TotalScore { get; set; }
}

// Hybrid query types
public class HybridQueryOptions
{
    public List<string>? Collections { get; init; }
    public int Limit { get; init; } = 10;
    public double MinScore { get; init; }
    public int CandidateLimit { get; init; } = 40;
    public bool Explain { get; init; }
    public string? Intent { get; init; }
    public bool SkipRerank { get; init; }
    public ChunkStrategy ChunkStrategy { get; init; } = ChunkStrategy.Regex;

    /// <summary>
    /// Populated by the pipeline with diagnostic information (vector-only flag, best scores).
    /// Pass a new instance to receive diagnostics; leave null to skip.
    /// </summary>
    public HybridQueryDiagnostics? Diagnostics { get; set; }
}

public class HybridQueryResult
{
    public string File { get; set; } = "";
    public string DisplayPath { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string BestChunk { get; set; } = "";
    public int BestChunkPos { get; set; }
    public double Score { get; set; }
    public string? Context { get; set; }
    public string Docid { get; set; } = "";
    public HybridQueryExplain? Explain { get; set; }
}

public record HybridQueryExplain(
    List<double> FtsScores, List<double> VectorScores,
    RrfScoreTrace? Rrf, double RerankScore, double BlendedScore);

/// <summary>
/// Diagnostic output populated by the hybrid pipeline so callers can
/// detect low-confidence scenarios (e.g. vector-only results).
/// </summary>
public class HybridQueryDiagnostics
{
    /// <summary>Whether any FTS/BM25 backend contributed results.</summary>
    public bool HasFtsResults { get; set; }
    /// <summary>Best raw cosine similarity score among vector backends.</summary>
    public double BestVecScore { get; set; }
    /// <summary>Best reranker score (0-1, from Qwen3-Reranker).</summary>
    public double BestRerankScore { get; set; }
    /// <summary>Gates that were relaxed because --min-score was below the configured threshold.</summary>
    public List<string> RelaxedGates { get; set; } = [];
}

public class StructuredSearchOptions
{
    public List<string>? Collections { get; init; }
    public int Limit { get; init; } = 10;
    public double MinScore { get; init; }
    public int CandidateLimit { get; init; } = 40;
    public bool Explain { get; init; }
    public string? Intent { get; init; }
    public bool SkipRerank { get; init; }
    public ChunkStrategy ChunkStrategy { get; init; } = ChunkStrategy.Regex;
}
