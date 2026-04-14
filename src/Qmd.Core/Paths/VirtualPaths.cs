using System.Text.RegularExpressions;

namespace Qmd.Core.Paths;

public record VirtualPath(string CollectionName, string Path);

/// <summary>
/// Utilities for qmd:// virtual path URIs.
/// </summary>
public static class VirtualPaths
{
    private static readonly Regex VirtualPathRegex = new(@"^qmd://([^/]+)/?(.*)$", RegexOptions.Compiled);

    /// <summary>
    /// Check if a path is explicitly a virtual path (starts with "qmd:" or "//").
    /// Does NOT consider bare collection/path.md as virtual.
    /// </summary>
    public static bool IsVirtualPath(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.StartsWith("qmd:")) return true;
        if (trimmed.StartsWith("//")) return true;
        return false;
    }

    /// <summary>
    /// Parse a virtual path like "qmd://collection-name/path/to/file.md" into components.
    /// Returns null if not a valid virtual path.
    /// </summary>
    public static VirtualPath? Parse(string virtualPath)
    {
        var normalized = Normalize(virtualPath);
        var match = VirtualPathRegex.Match(normalized);
        if (!match.Success || string.IsNullOrEmpty(match.Groups[1].Value))
            return null;

        return new VirtualPath(match.Groups[1].Value, match.Groups[2].Value);
    }

    /// <summary>
    /// Build a virtual path from collection name and relative path.
    /// </summary>
    public static string Build(string collectionName, string path)
    {
        return $"qmd://{collectionName}/{path}";
    }

    /// <summary>
    /// Normalize explicit virtual path formats to standard qmd:// format.
    /// Handles extra slashes and missing qmd: prefix.
    /// </summary>
    public static string Normalize(string input)
    {
        var path = input.Trim();

        // Handle qmd:// with extra slashes
        if (path.StartsWith("qmd:"))
        {
            path = path[4..];
            path = path.TrimStart('/');
            return $"qmd://{path}";
        }

        // Handle //collection/path (missing qmd: prefix)
        if (path.StartsWith("//"))
        {
            path = path.TrimStart('/');
            return $"qmd://{path}";
        }

        return path;
    }
}
