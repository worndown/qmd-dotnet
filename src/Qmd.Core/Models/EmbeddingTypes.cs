namespace Qmd.Core.Models;

public record EmbedProgress(int ChunksEmbedded, int TotalChunks, long BytesProcessed, long TotalBytes, int Errors);

public record EmbedResult(int DocsProcessed, int ChunksEmbedded, int Errors, long DurationMs);

public record PendingEmbeddingDoc(string Hash, string Path, long Bytes);

public record EmbeddingDoc(string Hash, string Path, long Bytes, string Body);

public record ChunkItem(string Hash, string Title, string Text, int Seq, int Pos, int Tokens, int Bytes);

public class EmbedPipelineOptions
{
    public bool Force { get; init; }
    public string? Model { get; init; }
    public int MaxDocsPerBatch { get; init; } = 64;
    public int MaxBatchBytes { get; init; } = 64 * 1024 * 1024;
    public ChunkStrategy ChunkStrategy { get; init; } = ChunkStrategy.Regex;
    public Action<EmbedProgress>? OnProgress { get; init; }
    public CancellationToken CancellationToken { get; init; }
}
