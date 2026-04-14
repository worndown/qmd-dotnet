using System.Text.RegularExpressions;

namespace Qmd.Core.Paths;

/// <summary>
/// FTS5 full-text search utility functions.
/// </summary>
internal static class FtsUtils
{
    private static readonly Regex NonFtsCharsRegex = new(@"[^\p{L}\p{N}'_]", RegexOptions.Compiled);

    /// <summary>
    /// Sanitize a term for safe use in FTS5 queries.
    /// Keeps Unicode letters, numbers, apostrophes, underscores. Lowercases.
    /// </summary>
    public static string SanitizeTerm(string term)
    {
        return NonFtsCharsRegex.Replace(term, "").ToLowerInvariant();
    }
}
