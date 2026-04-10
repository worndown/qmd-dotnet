using System.Text.RegularExpressions;
using Qmd.Core.Paths;

namespace Qmd.Core.Search;

public static class Fts5QueryBuilder
{
    private static readonly Regex HyphenatedRegex = new(
        @"^[\p{L}\p{N}][\p{L}\p{N}'-]*-[\p{L}\p{N}][\p{L}\p{N}'-]*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Parse natural language query into FTS5 syntax.
    /// Supports quoted phrases, negation (-term), hyphenated tokens, prefix matching.
    /// </summary>
    public static string? BuildFTS5Query(string query)
    {
        var positive = new List<string>();
        var negative = new List<string>();

        int i = 0;
        var s = query.Trim();

        while (i < s.Length)
        {
            // Skip whitespace
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length) break;

            // Check for negation prefix
            bool negated = s[i] == '-';
            if (negated) i++;

            if (i >= s.Length) break;

            // Check for quoted phrase
            if (s[i] == '"')
            {
                int start = i + 1;
                i++;
                while (i < s.Length && s[i] != '"') i++;
                var phrase = s[start..i].Trim();
                if (i < s.Length) i++; // skip closing quote

                if (phrase.Length > 0)
                {
                    var sanitized = string.Join(' ',
                        phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                            .Select(FtsUtils.SanitizeTerm)
                            .Where(t => t.Length > 0));
                    if (sanitized.Length > 0)
                    {
                        var ftsPhrase = $"\"{sanitized}\"";
                        (negated ? negative : positive).Add(ftsPhrase);
                    }
                }
            }
            else
            {
                // Plain term (until whitespace or quote)
                int start = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '"') i++;
                var term = s[start..i];

                if (IsHyphenatedToken(term))
                {
                    var sanitized = SanitizeHyphenatedTerm(term);
                    if (sanitized.Length > 0)
                    {
                        var ftsPhrase = $"\"{sanitized}\"";
                        (negated ? negative : positive).Add(ftsPhrase);
                    }
                }
                else
                {
                    var sanitized = FtsUtils.SanitizeTerm(term);
                    if (sanitized.Length > 0)
                    {
                        var ftsTerm = $"\"{sanitized}\"*";
                        (negated ? negative : positive).Add(ftsTerm);
                    }
                }
            }
        }

        if (positive.Count == 0) return null;

        var result = string.Join(" AND ", positive);
        foreach (var neg in negative)
            result = $"{result} NOT {neg}";

        return result;
    }

    public static bool IsHyphenatedToken(string token)
    {
        return HyphenatedRegex.IsMatch(token);
    }

    public static string SanitizeHyphenatedTerm(string term)
    {
        return string.Join(' ',
            term.Split('-')
                .Select(FtsUtils.SanitizeTerm)
                .Where(t => t.Length > 0));
    }
}
