namespace Qmd.Core.Chunking;

public static class ChunkConstants
{
    // Token-based limits (used by AST chunking with CharBasedTokenizer at 3 chars/token)
    public const int ChunkSizeTokens = 900;
    public const int ChunkOverlapTokens = 135;  // 15% of 900
    public const int ChunkWindowTokens = 200;

    // Char-based limits (used directly by regex chunking, independent of token estimation)
    public const int ChunkSizeChars = 3600;
    public const int ChunkOverlapChars = 540;
    public const int ChunkWindowChars = 800;
}
