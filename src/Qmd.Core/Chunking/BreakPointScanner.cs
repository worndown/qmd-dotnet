using System.Text.RegularExpressions;
using Qmd.Core.Models;

namespace Qmd.Core.Chunking;

internal static class BreakPointScanner
{
    private static readonly (Regex Pattern, double Score, string Type)[] BreakPatterns =
    [
        (new Regex(@"\n#{1}(?!#)", RegexOptions.Compiled), 100, "h1"),
        (new Regex(@"\n#{2}(?!#)", RegexOptions.Compiled), 90, "h2"),
        (new Regex(@"\n#{3}(?!#)", RegexOptions.Compiled), 80, "h3"),
        (new Regex(@"\n#{4}(?!#)", RegexOptions.Compiled), 70, "h4"),
        (new Regex(@"\n#{5}(?!#)", RegexOptions.Compiled), 60, "h5"),
        (new Regex(@"\n#{6}(?!#)", RegexOptions.Compiled), 50, "h6"),
        (new Regex(@"\n```", RegexOptions.Compiled), 80, "codeblock"),
        (new Regex(@"\n(?:---|\*\*\*|___)\s*\n", RegexOptions.Compiled), 60, "hr"),
        (new Regex(@"\n\n+", RegexOptions.Compiled), 20, "blank"),
        (new Regex(@"\n[-*]\s", RegexOptions.Compiled), 5, "list"),
        (new Regex(@"\n\d+\.\s", RegexOptions.Compiled), 5, "numlist"),
        (new Regex(@"\n", RegexOptions.Compiled), 1, "newline"),
    ];

    /// <summary>
    /// Scan text for all potential break points.
    /// Returns sorted array with higher-scoring patterns taking precedence at same position.
    /// </summary>
    public static List<BreakPoint> ScanBreakPoints(string text)
    {
        var seen = new Dictionary<int, BreakPoint>();

        foreach (var (pattern, score, type) in BreakPatterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var pos = match.Index;
                if (!seen.TryGetValue(pos, out var existing) || score > existing.Score)
                {
                    seen[pos] = new BreakPoint(pos, score, type);
                }
            }
        }

        var result = seen.Values.ToList();
        result.Sort((a, b) => a.Pos.CompareTo(b.Pos));
        return result;
    }

    /// <summary>
    /// Find all code fence regions (between ``` markers).
    /// </summary>
    public static List<CodeFenceRegion> FindCodeFences(string text)
    {
        var regions = new List<CodeFenceRegion>();
        var fencePattern = new Regex(@"\n```", RegexOptions.Compiled);
        bool inFence = false;
        int fenceStart = 0;

        foreach (Match match in fencePattern.Matches(text))
        {
            if (!inFence)
            {
                fenceStart = match.Index;
                inFence = true;
            }
            else
            {
                regions.Add(new CodeFenceRegion(fenceStart, match.Index + match.Length));
                inFence = false;
            }
        }

        // Handle unclosed fence
        if (inFence)
        {
            regions.Add(new CodeFenceRegion(fenceStart, text.Length));
        }

        return regions;
    }

    /// <summary>
    /// Check if a position is inside a code fence region.
    /// </summary>
    public static bool IsInsideCodeFence(int pos, List<CodeFenceRegion> fences)
    {
        return fences.Any(f => pos > f.Start && pos < f.End);
    }

    /// <summary>
    /// Find the best cut position using scored break points with squared distance decay.
    /// </summary>
    public static int FindBestCutoff(List<BreakPoint> breakPoints, int targetCharPos,
        int windowChars = ChunkConstants.ChunkWindowChars, double decayFactor = 0.7,
        List<CodeFenceRegion>? codeFences = null)
    {
        codeFences ??= [];
        var windowStart = targetCharPos - windowChars;
        double bestScore = -1;
        int bestPos = targetCharPos;

        foreach (var bp in breakPoints)
        {
            if (bp.Pos < windowStart) continue;
            if (bp.Pos > targetCharPos) break; // sorted

            if (IsInsideCodeFence(bp.Pos, codeFences)) continue;

            var distance = targetCharPos - bp.Pos;
            var normalizedDist = (double)distance / windowChars;
            var multiplier = 1.0 - (normalizedDist * normalizedDist) * decayFactor;
            var finalScore = bp.Score * multiplier;

            if (finalScore > bestScore)
            {
                bestScore = finalScore;
                bestPos = bp.Pos;
            }
        }

        return bestPos;
    }

    /// <summary>
    /// Merge two sets of break points, keeping highest score at each position.
    /// </summary>
    public static List<BreakPoint> MergeBreakPoints(List<BreakPoint> a, List<BreakPoint> b)
    {
        var seen = new Dictionary<int, BreakPoint>();
        foreach (var bp in a)
        {
            if (!seen.TryGetValue(bp.Pos, out var existing) || bp.Score > existing.Score)
                seen[bp.Pos] = bp;
        }
        foreach (var bp in b)
        {
            if (!seen.TryGetValue(bp.Pos, out var existing) || bp.Score > existing.Score)
                seen[bp.Pos] = bp;
        }
        var result = seen.Values.ToList();
        result.Sort((a, b) => a.Pos.CompareTo(b.Pos));
        return result;
    }
}
