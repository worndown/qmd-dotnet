using System.Text.RegularExpressions;

namespace Qmd.Core.Search;

internal static class QueryValidator
{
    public static string? ValidateSemanticQuery(string query)
    {
        if (Regex.IsMatch(query, @"-\w") || Regex.IsMatch(query, @"-"""))
            return "Negation (-term) is not supported in vec/hyde queries. Use lex for exclusions.";
        return null;
    }

    public static string? ValidateLexQuery(string query)
    {
        if (Regex.IsMatch(query, @"[\r\n]"))
            return "Lex queries must be a single line. Remove newline characters or split into separate lex: lines.";
        var quoteCount = query.Count(c => c == '"');
        if (quoteCount % 2 == 1)
            return "Lex query has an unmatched double quote (\"). Add the closing quote or remove it.";
        return null;
    }
}
