namespace Qmd.Core.Bench;

/// <summary>
/// Scoring functions for the QMD benchmark harness.
/// Computes precision@k, recall, MRR, and F1 for search results
/// against ground-truth expected files.
/// </summary>
public static class BenchmarkScorer
{
    /// <summary>
    /// Normalize a file path for comparison.
    /// Strips qmd:// prefix, lowercases, removes leading/trailing slashes.
    /// </summary>
    public static string NormalizePath(string p)
    {
        if (p.StartsWith("qmd://"))
        {
            // qmd://collection/path/to/file -> path/to/file
            var withoutScheme = p["qmd://".Length..];
            var slashIdx = withoutScheme.IndexOf('/');
            p = slashIdx >= 0 ? withoutScheme[(slashIdx + 1)..] : withoutScheme;
        }

        return p.ToLowerInvariant().Trim('/');
    }

    /// <summary>
    /// Check if two paths refer to the same file.
    /// Handles different path formats by comparing normalized suffixes.
    /// </summary>
    public static bool PathsMatch(string result, string expected)
    {
        var nr = NormalizePath(result);
        var ne = NormalizePath(expected);
        if (nr == ne) return true;
        if (nr.EndsWith(ne) || ne.EndsWith(nr)) return true;
        return false;
    }

    /// <summary>
    /// Score a set of search results against expected files.
    /// </summary>
    public static ScoreResult ScoreResults(List<string> resultFiles, List<string> expectedFiles, int topK)
    {
        // Count hits in top-k
        var topKResults = resultFiles.Take(topK).ToList();
        int hitsAtK = 0;
        foreach (var expected in expectedFiles)
        {
            if (topKResults.Any(r => PathsMatch(r, expected)))
                hitsAtK++;
        }

        // Count total hits anywhere
        int totalHits = 0;
        foreach (var expected in expectedFiles)
        {
            if (resultFiles.Any(r => PathsMatch(r, expected)))
                totalHits++;
        }

        // MRR: reciprocal rank of first relevant result
        double mrr = 0;
        for (int i = 0; i < resultFiles.Count; i++)
        {
            if (expectedFiles.Any(e => PathsMatch(resultFiles[i], e)))
            {
                mrr = 1.0 / (i + 1);
                break;
            }
        }

        int denominator = Math.Min(topK, expectedFiles.Count);
        double precisionAtK = denominator > 0 ? (double)hitsAtK / denominator : 0;
        double recall = expectedFiles.Count > 0 ? (double)totalHits / expectedFiles.Count : 0;
        double f1 = precisionAtK + recall > 0
            ? 2 * (precisionAtK * recall) / (precisionAtK + recall)
            : 0;

        return new ScoreResult(precisionAtK, recall, mrr, f1, hitsAtK);
    }
}

/// <summary>
/// Result of scoring search results against expected files.
/// </summary>
public record ScoreResult(
    double PrecisionAtK,
    double Recall,
    double Mrr,
    double F1,
    int HitsAtK);
