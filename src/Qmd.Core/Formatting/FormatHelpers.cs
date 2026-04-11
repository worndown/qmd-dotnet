using System.Text;
using System.Text.RegularExpressions;

namespace Qmd.Core.Formatting;

public static class FormatHelpers
{
    private static readonly bool NoColor =
        Environment.GetEnvironmentVariable("NO_COLOR") != null;

    // ANSI escape codes matching TS terminal colors (qmd.ts:183-194)
    public static string Reset => NoColor ? "" : "\x1b[0m";
    public static string Dim => NoColor ? "" : "\x1b[2m";
    public static string Bold => NoColor ? "" : "\x1b[1m";
    public static string Cyan => NoColor ? "" : "\x1b[36m";
    public static string Yellow => NoColor ? "" : "\x1b[33m";
    public static string Green => NoColor ? "" : "\x1b[32m";

    /// <summary>
    /// Format score with color based on value (matches TS formatScore).
    /// Green >= 70%, yellow >= 40%, dim otherwise.
    /// </summary>
    public static string FormatScore(double score)
    {
        var pct = ((int)(score * 100)).ToString().PadLeft(3);
        if (NoColor) return $"{pct}%";
        if (score >= 0.7) return $"{Green}{pct}%{Reset}";
        if (score >= 0.4) return $"{Yellow}{pct}%{Reset}";
        return $"{Dim}{pct}%{Reset}";
    }

    public static string EscapeCsv(string? value)
    {
        if (value == null) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    public static string EscapeXml(string str)
    {
        return str
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    public static string AddLineNumbers(string text, int startLine = 1)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append($"{startLine + i}: {lines[i]}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Highlight query terms in text using ANSI bold+yellow.
    /// Only applies when NO_COLOR is not set.
    /// </summary>
    public static string HighlightTerms(string text, string? query)
    {
        if (NoColor || string.IsNullOrEmpty(query)) return text;

        var terms = query.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .Distinct()
            .ToArray();

        if (terms.Length == 0) return text;

        // Build regex alternation for all terms (case-insensitive)
        var pattern = string.Join("|", terms.Select(Regex.Escape));
        return Regex.Replace(text, pattern,
            m => $"\x1b[1;33m{m.Value}\x1b[0m",
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Create an OSC 8 terminal hyperlink for file paths.
    /// editorUri template uses {path} and {line} placeholders.
    /// Example: "vscode://file/{path}:{line}"
    /// </summary>
    public static string MakeTerminalLink(string displayText, string? editorUri, string? filePath, int line = 1)
    {
        if (NoColor || string.IsNullOrEmpty(editorUri) || string.IsNullOrEmpty(filePath))
            return displayText;

        var encodedPath = Uri.EscapeDataString(filePath)
            .Replace("%2F", "/")
            .Replace("%5C", "/");
        var uri = editorUri
            .Replace("{path}", encodedPath)
            .Replace("{line}", line.ToString());

        return $"\x1b]8;;{uri}\x07{displayText}\x1b]8;;\x07";
    }
}
