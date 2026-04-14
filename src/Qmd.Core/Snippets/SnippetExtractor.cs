using Qmd.Core.Chunking;
using Qmd.Core.Models;

namespace Qmd.Core.Snippets;

public static class SnippetExtractor
{
    public const double IntentWeightSnippet = 0.3;
    public const double IntentWeightChunk = 0.5;

    public static SnippetResult ExtractSnippet(string body, string query, int maxLen = 500,
        int? chunkPos = null, int? chunkLen = null, string? intent = null)
    {
        var totalLines = body.Split('\n').Length;
        var searchBody = body;
        int lineOffset = 0;

        if (chunkPos.HasValue && chunkPos.Value > 0 && chunkPos.Value < body.Length)
        {
            var searchLen = chunkLen ?? ChunkConstants.ChunkSizeChars;
            var contextStart = Math.Max(0, chunkPos.Value - 100);
            var contextEnd = Math.Min(body.Length, chunkPos.Value + searchLen + 100);
            searchBody = body[contextStart..contextEnd];
            if (contextStart > 0)
                lineOffset = body[..contextStart].Split('\n').Length - 1;
        }

        var lines = searchBody.Split('\n');
        var queryTerms = query.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var intentTerms = intent != null ? IntentProcessor.ExtractIntentTerms(intent) : [];

        int bestLine = 0;
        double bestScore = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            var lineLower = lines[i].ToLowerInvariant();
            double score = 0;
            foreach (var term in queryTerms)
                if (lineLower.Contains(term)) score += 1.0;
            foreach (var term in intentTerms)
                if (lineLower.Contains(term)) score += IntentWeightSnippet;

            if (score > bestScore)
            {
                bestScore = score;
                bestLine = i;
            }
        }

        var start = Math.Max(0, bestLine - 1);
        var end = Math.Min(lines.Length, bestLine + 3);
        var snippetLines = lines[start..end];
        var snippetText = string.Join('\n', snippetLines);

        // Fallback: if chunk-scoped search produced empty snippet, retry full document
        if (chunkPos.HasValue && chunkPos.Value > 0 && string.IsNullOrWhiteSpace(snippetText))
            return ExtractSnippet(body, query, maxLen, null, null, intent);

        if (snippetText.Length > maxLen)
            snippetText = snippetText[..(maxLen - 3)] + "...";

        var absoluteStart = lineOffset + start + 1; // 1-indexed
        var snippetLineCount = snippetLines.Length;
        var linesBefore = absoluteStart - 1;
        var linesAfter = totalLines - (absoluteStart + snippetLineCount - 1);

        var header = $"@@ -{absoluteStart},{snippetLineCount} @@ ({linesBefore} before, {linesAfter} after)";
        var snippet = $"{header}\n{snippetText}";

        return new SnippetResult(
            Line: lineOffset + bestLine + 1,
            Snippet: snippet,
            LinesBefore: linesBefore,
            LinesAfter: linesAfter,
            SnippetLines: snippetLineCount);
    }
}
