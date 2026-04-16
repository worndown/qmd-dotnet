using Qmd.Core.Llm;

namespace Qmd.Core.Models;

public record EmbedProgress(int ChunksEmbedded, int TotalChunks, long BytesProcessed, long TotalBytes, int Errors);

public record EmbedResult(int DocsProcessed, int ChunksEmbedded, int Errors, long DurationMs);

public record PendingEmbeddingDoc(string Hash, string Path, long Bytes);

public record EmbeddingDoc(string Hash, string Path, long Bytes, string Body);

public record ChunkItem(string Hash, string Title, string Text, int Seq, int Pos, int Tokens, int Bytes);

public class EmbeddingProfileOptions
{
    /// <summary>Number of random chunks to sample as queries.</summary>
    public int SampleSize { get; init; } = 100;

    /// <summary>Restrict profiling to these collections.</summary>
    public List<string>? Collections { get; init; }
}

public class EmbeddingProfile
{
    public string Model { get; init; } = "";
    public int Dimensions { get; init; }
    public int TotalChunks { get; init; }
    public int SampleSize { get; init; }
    public int ScoreCount { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double Mean { get; init; }
    public double Median { get; init; }
    public double P5 { get; init; }
    public double P25 { get; init; }
    public double P75 { get; init; }
    public double P95 { get; init; }

    /// <summary>Suggested --min-score for vsearch, based on P75 of inter-document similarity.</summary>
    public double SuggestedVsearchMinScore { get; init; }
}

public class EmbedPipelineOptions
{
    public bool Force { get; init; }
    public string? Model { get; init; }
    public int MaxDocsPerBatch { get; init; } = LlmConstants.DefaultMaxDocsPerBatch;
    public int MaxBatchBytes { get; init; } = LlmConstants.DefaultMaxBatchBytes;
    public ChunkStrategy ChunkStrategy { get; init; } = ChunkStrategy.Regex;
    public IProgress<EmbedProgress>? Progress { get; init; }
    public CancellationToken CancellationToken { get; init; }
}
