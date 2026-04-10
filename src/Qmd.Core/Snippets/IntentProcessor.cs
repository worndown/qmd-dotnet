using System.Text.RegularExpressions;

namespace Qmd.Core.Snippets;

public static class IntentProcessor
{
    private static readonly Regex PunctuationStripRegex = new(
        @"^[^\p{L}\p{N}]+|[^\p{L}\p{N}]+$", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        // 2-char function words
        "am", "an", "as", "at", "be", "by", "do", "he", "if",
        "in", "is", "it", "me", "my", "no", "of", "on", "or", "so",
        "to", "up", "us", "we",
        // 3-char function words
        "all", "and", "any", "are", "but", "can", "did", "for", "get",
        "has", "her", "him", "his", "how", "its", "let", "may", "not",
        "our", "out", "the", "too", "was", "who", "why", "you",
        // 4+ char common words
        "also", "does", "find", "from", "have", "into", "more", "need",
        "show", "some", "tell", "that", "them", "this", "want", "what",
        "when", "will", "with", "your",
        // Search-context noise
        "about", "looking", "notes", "search", "where", "which",
    };

    /// <summary>
    /// Extract meaningful terms from an intent string, filtering stop words and punctuation.
    /// Returns lowercase terms suitable for text matching.
    /// </summary>
    public static List<string> ExtractIntentTerms(string intent)
    {
        return intent.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => PunctuationStripRegex.Replace(t, ""))
            .Where(t => t.Length > 1 && !StopWords.Contains(t))
            .ToList();
    }
}
