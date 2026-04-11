using System.Text.RegularExpressions;

namespace Qmd.Core.Paths;

/// <summary>
/// Utilities for document ID (docid) handling.
/// A docid is the first 6 characters of a document's SHA256 hash.
/// </summary>
internal static class DocidUtils
{
    private static readonly Regex HexRegex = new(@"^[a-f0-9]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Get the docid (first 6 chars) from a full hash.
    /// </summary>
    public static string GetDocid(string hash)
    {
        return hash[..6];
    }

    /// <summary>
    /// Normalize a docid by stripping #, quotes, and whitespace.
    /// </summary>
    public static string Normalize(string docid)
    {
        var normalized = docid.Trim();

        // Strip surrounding quotes (single or double)
        if ((normalized.StartsWith('"') && normalized.EndsWith('"')) ||
            (normalized.StartsWith('\'') && normalized.EndsWith('\'')))
        {
            normalized = normalized[1..^1];
        }

        // Strip leading #
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];

        return normalized;
    }

    /// <summary>
    /// Check if a string looks like a docid reference (6+ hex chars after normalization).
    /// </summary>
    public static bool IsDocid(string input)
    {
        var normalized = Normalize(input);
        return normalized.Length >= 6 && HexRegex.IsMatch(normalized);
    }
}
