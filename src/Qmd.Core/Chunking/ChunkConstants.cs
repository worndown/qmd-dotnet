namespace Qmd.Core.Chunking;

internal static class ChunkConstants
{
    public const int AvgCharsPerToken = 3;

    // Token-based limits (used by token-aware chunking)
    public const int ChunkSizeTokens = 900;
    public const int ChunkOverlapTokens = 135;  // 15% of 900
    public const int ChunkWindowTokens = 200;

    // Char-based limits (used directly by regex chunking, independent of token estimation)
    public const int ChunkSizeChars = 3600;
    public const int ChunkOverlapChars = 540;
    public const int ChunkWindowChars = 800;
}
