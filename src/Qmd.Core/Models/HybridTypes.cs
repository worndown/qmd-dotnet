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
