using DotNet.Globbing;
using Qmd.Core.Database;

namespace Qmd.Core.Retrieval;

/// <summary>
/// Matches files in the database against glob patterns.
/// Uses DotNet.Glob for in-memory glob matching against three path forms:
/// virtual path (qmd://collection/path), relative path, and collection/path.
/// </summary>
internal static class GlobMatcher
{
    public record GlobMatch(string VirtualPath, string DisplayPath, int BodyLength);

    /// <summary>
    /// Match active documents against a glob pattern.
    /// Tests each document against three path forms for maximum flexibility.
    /// </summary>
    public static List<GlobMatch> MatchFilesByGlob(IQmdDatabase db, string pattern)
    {
        var allFiles = db.Prepare(@"
            SELECT
                'qmd://' || d.collection || '/' || d.path AS virtual_path,
                LENGTH(c.doc) AS body_length,
                d.path,
                d.collection
            FROM documents d
            JOIN content c ON c.hash = d.hash
            WHERE d.active = 1
        ").All<GlobFileRow>();

        var glob = Glob.Parse(pattern);
        var results = new List<GlobMatch>();

        foreach (var f in allFiles)
        {
            var virtualPath = f.VirtualPath;
            var path = f.Path;
            var collection = f.Collection;
            var bodyLength = f.BodyLength;
            var collectionPath = $"{collection}/{path}";

            if (glob.IsMatch(virtualPath) || glob.IsMatch(path) || glob.IsMatch(collectionPath))
            {
                results.Add(new GlobMatch(virtualPath, path, bodyLength));
            }
        }

        return results;
    }
}
