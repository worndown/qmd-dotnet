using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Qmd.Core.Paths;

/// <summary>
/// Converts filesystem paths to URL-safe, token-friendly format.
/// </summary>
internal static class Handelize
{
    private static readonly Regex NonWordRegex = new(@"[^\p{L}\p{N}$]+", RegexOptions.Compiled);
    private static readonly Regex LeadTrailDashRegex = new(@"^-+|-+$", RegexOptions.Compiled);
    private static readonly Regex ExtensionRegex = new(@"(\.[a-z0-9]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ValidContentRegexBmp = new(@"[\p{L}\p{N}\p{So}\p{Sk}$]", RegexOptions.Compiled);
    private static readonly Regex EmojiRegex = new(@"(?:\p{So}\p{Mn}?|\p{Sk})+", RegexOptions.Compiled);

    /// <summary>
    /// Convert a path to a token-friendly representation.
    /// Lowercase, emoji→hex, special chars→dashes, ___→/, preserves Unicode letters.
    /// </summary>
    public static string Convert(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("handelize: path cannot be empty");

        // Validate that the filename has usable content
        var segments = path.Split('/').Where(s => s.Length > 0).ToArray();
        var lastSegment = segments.Length > 0 ? segments[^1] : "";
        var filenameWithoutExt = ExtensionRegex.Replace(lastSegment, "");
        if (!HasValidContent(filenameWithoutExt))
            throw new ArgumentException($"handelize: path \"{path}\" has no valid filename content");

        var allSegments = path
            .Replace("___", "/")
            .ToLowerInvariant()
            .Split('/')
            .Where(s => s.Length > 0)
            .ToArray();

        var result = new List<string>();
        for (int i = 0; i < allSegments.Length; i++)
        {
            var segment = EmojiToHex(allSegments[i]);
            bool isLast = i == allSegments.Length - 1;

            if (isLast)
            {
                var extMatch = ExtensionRegex.Match(segment);
                var ext = extMatch.Success ? extMatch.Groups[1].Value : "";
                var nameWithoutExt = ext.Length > 0 ? segment[..^ext.Length] : segment;
                var cleanedName = LeadTrailDashRegex.Replace(NonWordRegex.Replace(nameWithoutExt, "-"), "");
                var val = cleanedName + ext;
                if (!string.IsNullOrEmpty(val)) result.Add(val);
            }
            else
            {
                var val = LeadTrailDashRegex.Replace(NonWordRegex.Replace(segment, "-"), "");
                if (!string.IsNullOrEmpty(val)) result.Add(val);
            }
        }

        var joined = string.Join('/', result);
        if (string.IsNullOrEmpty(joined))
            throw new ArgumentException($"handelize: path \"{path}\" resulted in empty string after processing");

        return joined;
    }

    /// <summary>
    /// Check if a string has valid content (letters, numbers, symbols, or $).
    /// Handles supplementary Unicode characters (emoji) that .NET regex \p{So} misses.
    /// </summary>
    private static bool HasValidContent(string s)
    {
        if (ValidContentRegexBmp.IsMatch(s)) return true;
        // Check supplementary characters (surrogate pairs)
        var enumerator = StringInfo.GetTextElementEnumerator(s);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            if (element.Length > 0)
            {
                var cp = char.ConvertToUtf32(element, 0);
                if (cp > 0xFFFF)
                {
                    var cat = CharUnicodeInfo.GetUnicodeCategory(cp);
                    if (cat == UnicodeCategory.OtherSymbol || cat == UnicodeCategory.ModifierSymbol)
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Replace emoji/symbol codepoints with their hex representation.
    /// Handles both BMP and supplementary (surrogate pair) characters.
    /// </summary>
    private static string EmojiToHex(string str)
    {
        var sb = new StringBuilder();
        var enumerator = StringInfo.GetTextElementEnumerator(str);
        bool lastWasEmoji = false;

        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var cp = char.ConvertToUtf32(element, 0);
            var cat = CharUnicodeInfo.GetUnicodeCategory(cp);

            if (cat == UnicodeCategory.OtherSymbol || cat == UnicodeCategory.ModifierSymbol)
            {
                if (lastWasEmoji) sb.Append('-');
                sb.Append(cp.ToString("x"));
                lastWasEmoji = true;
            }
            else if (cat == UnicodeCategory.NonSpacingMark && lastWasEmoji)
            {
                // Skip combining marks after emoji (e.g. skin tone modifiers)
            }
            else
            {
                lastWasEmoji = false;
                sb.Append(element);
            }
        }

        return sb.ToString();
    }
}
