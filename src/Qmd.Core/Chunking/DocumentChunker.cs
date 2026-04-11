using Qmd.Core.Models;

namespace Qmd.Core.Chunking;

internal static class DocumentChunker
{
    /// <summary>
    /// Core chunking algorithm using precomputed break points and code fences.
    /// </summary>
    public static List<TextChunk> ChunkDocumentWithBreakPoints(
        string content,
        List<BreakPoint> breakPoints,
        List<CodeFenceRegion> codeFences,
        int maxChars = ChunkConstants.ChunkSizeChars,
        int overlapChars = ChunkConstants.ChunkOverlapChars,
        int windowChars = ChunkConstants.ChunkWindowChars)
    {
        if (content.Length <= maxChars)
            return [new TextChunk(content, 0)];

        var chunks = new List<TextChunk>();
        int charPos = 0;

        while (charPos < content.Length)
        {
            var targetEndPos = Math.Min(charPos + maxChars, content.Length);
            var endPos = targetEndPos;

            if (endPos < content.Length)
            {
                var bestCutoff = BreakPointScanner.FindBestCutoff(
                    breakPoints, targetEndPos, windowChars, 0.7, codeFences);

                if (bestCutoff > charPos && bestCutoff <= targetEndPos)
                    endPos = bestCutoff;
            }

            if (endPos <= charPos)
                endPos = Math.Min(charPos + maxChars, content.Length);

            chunks.Add(new TextChunk(content[charPos..endPos], charPos));

            if (endPos >= content.Length) break;

            charPos = endPos - overlapChars;
            var lastChunkPos = chunks[^1].Pos;
            if (charPos <= lastChunkPos)
                charPos = endPos;
        }

        return chunks;
    }

    /// <summary>
    /// Chunk a document using regex-only break point detection.
    /// </summary>
    public static List<TextChunk> ChunkDocument(
        string content,
        int maxChars = ChunkConstants.ChunkSizeChars,
        int overlapChars = ChunkConstants.ChunkOverlapChars,
        int windowChars = ChunkConstants.ChunkWindowChars)
    {
        var breakPoints = BreakPointScanner.ScanBreakPoints(content);
        var codeFences = BreakPointScanner.FindCodeFences(content);
        return ChunkDocumentWithBreakPoints(content, breakPoints, codeFences, maxChars, overlapChars, windowChars);
    }

    /// <summary>
    /// Chunk a document with optional AST-aware break point detection.
    /// When strategy is Auto and filepath identifies a supported code language,
    /// AST break points are merged with regex break points for better code splitting.
    /// </summary>
    public static List<TextChunk> ChunkDocument(
        string content,
        string? filepath,
        ChunkStrategy strategy,
        int maxChars = ChunkConstants.ChunkSizeChars,
        int overlapChars = ChunkConstants.ChunkOverlapChars,
        int windowChars = ChunkConstants.ChunkWindowChars)
    {
        var breakPoints = BreakPointScanner.ScanBreakPoints(content);
        var codeFences = BreakPointScanner.FindCodeFences(content);

        if (strategy == ChunkStrategy.Auto && filepath != null)
        {
            var astPoints = AstBreakPointScanner.GetASTBreakPoints(content, filepath);
            if (astPoints.Count > 0)
            {
                breakPoints = BreakPointScanner.MergeBreakPoints(breakPoints, astPoints);
            }
        }

        return ChunkDocumentWithBreakPoints(content, breakPoints, codeFences, maxChars, overlapChars, windowChars);
    }

    /// <summary>
    /// Two-stage adaptive token-based chunking.
    /// Stage 1: Char-based chunking with 3 chars/token estimate (AST-aware when filepath provided).
    /// Stage 2: Token validation and adaptive re-splitting for chunks exceeding the limit.
    /// </summary>
    public static List<TokenizedChunk> ChunkDocumentByTokens(
        ITokenizer tokenizer,
        string content,
        int maxTokens = ChunkConstants.ChunkSizeTokens,
        int overlapTokens = ChunkConstants.ChunkOverlapTokens,
        int windowTokens = ChunkConstants.ChunkWindowTokens,
        string? filepath = null,
        ChunkStrategy chunkStrategy = ChunkStrategy.Regex,
        CancellationToken cancellationToken = default)
    {
        // Stage 1: Character-based initial chunking with conservative estimate
        // Use 3 chars/token (prose ~4, code ~2, mixed ~3)
        const int avgCharsPerToken = 3;
        var maxChars = maxTokens * avgCharsPerToken;
        var overlapChars = overlapTokens * avgCharsPerToken;
        var windowChars = windowTokens * avgCharsPerToken;

        var charChunks = ChunkDocument(content, filepath, chunkStrategy, maxChars, overlapChars, windowChars);

        // Stage 2: Tokenize and re-split any chunks that exceed the limit
        var results = new List<TokenizedChunk>();

        foreach (var chunk in charChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tokens = tokenizer.CountTokens(chunk.Text);

            if (tokens <= maxTokens)
            {
                results.Add(new TokenizedChunk(chunk.Text, chunk.Pos, tokens));
            }
            else
            {
                // Re-split with actual chars/token ratio and 5% safety margin
                var actualCharsPerToken = chunk.Text.Length / (double)tokens;
                var safeMaxChars = (int)(maxTokens * actualCharsPerToken * 0.95);
                safeMaxChars = Math.Max(safeMaxChars, 1);

                // Replicate TS overlap/window calculation for sub-chunks
                var subOverlap = (int)(overlapChars * actualCharsPerToken / 2);
                var subWindow = (int)(windowChars * actualCharsPerToken / 2);

                var subChunks = ChunkDocument(chunk.Text, safeMaxChars, subOverlap, subWindow);

                foreach (var subChunk in subChunks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var subTokens = tokenizer.CountTokens(subChunk.Text);
                    results.Add(new TokenizedChunk(subChunk.Text, chunk.Pos + subChunk.Pos, subTokens));
                }
            }
        }

        return results;
    }
}
