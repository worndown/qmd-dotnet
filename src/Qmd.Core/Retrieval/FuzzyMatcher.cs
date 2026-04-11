using Qmd.Core.Database;

namespace Qmd.Core.Retrieval;

internal static class FuzzyMatcher
{
    /// <summary>
    /// Classic Levenshtein distance (edit distance) between two strings.
    /// </summary>
    public static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var matrix = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) matrix[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) matrix[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[a.Length, b.Length];
    }

    /// <summary>
    /// Find files similar to the query using Levenshtein distance.
    /// </summary>
    public static List<string> FindSimilarFiles(IQmdDatabase db, string query, int maxDistance = 3, int limit = 5)
    {
        var allFiles = db.Prepare("SELECT path FROM documents WHERE active = 1").AllDynamic();
        var queryLower = query.ToLowerInvariant();

        return allFiles
            .Select(f => (path: f["path"]!.ToString()!, dist: Levenshtein(f["path"]!.ToString()!.ToLowerInvariant(), queryLower)))
            .Where(x => x.dist <= maxDistance)
            .OrderBy(x => x.dist)
            .Take(limit)
            .Select(x => x.path)
            .ToList();
    }
}
