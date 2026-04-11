using System.Text.RegularExpressions;

namespace Qmd.Core.Content;

internal static class TitleExtractor
{
    private static readonly Regex MdHeadingRegex = new(@"^##?\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MdH2Regex = new(@"^##\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex OrgTitleRegex = new(@"^#\+TITLE:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OrgHeadingRegex = new(@"^\*+\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);

    public static string ExtractTitle(string content, string filename)
    {
        var dotIdx = filename.LastIndexOf('.');
        var ext = dotIdx >= 0 ? filename[dotIdx..].ToLowerInvariant() : "";

        string? title = ext switch
        {
            ".md" => ExtractMarkdownTitle(content),
            ".org" => ExtractOrgTitle(content),
            _ => null
        };

        if (title != null) return title;

        // Fallback: filename without extension, last path segment
        var name = Regex.Replace(filename, @"\.[^.]+$", "");
        var parts = name.Split('/');
        return parts.Length > 0 ? parts[^1] : filename;
    }

    private static string? ExtractMarkdownTitle(string content)
    {
        var match = MdHeadingRegex.Match(content);
        if (!match.Success) return null;

        var title = match.Groups[1].Value.Trim();
        // Skip generic "Notes" titles, try next h2
        if (title is "📝 Notes" or "Notes")
        {
            var next = MdH2Regex.Match(content);
            if (next.Success) return next.Groups[1].Value.Trim();
        }
        return title;
    }

    private static string? ExtractOrgTitle(string content)
    {
        var titleProp = OrgTitleRegex.Match(content);
        if (titleProp.Success) return titleProp.Groups[1].Value.Trim();

        var heading = OrgHeadingRegex.Match(content);
        if (heading.Success) return heading.Groups[1].Value.Trim();

        return null;
    }
}
