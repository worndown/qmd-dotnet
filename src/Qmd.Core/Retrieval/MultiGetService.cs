using Qmd.Core.Database;
using Qmd.Core.Models;

namespace Qmd.Core.Retrieval;

/// <summary>
/// Multi-get pipeline: retrieve multiple documents by glob pattern or comma-separated list.
/// </summary>
internal static class MultiGetService
{
    private const int DefaultMaxBytes = 10 * 1024; // 10KB

    /// <summary>
    /// Find documents matching a pattern (glob or comma-separated list of paths/docids).
    /// </summary>
    public static (List<MultiGetResult> Docs, List<string> Errors) FindDocuments(
        IQmdDatabase db, string pattern, bool includeBody = false, int maxBytes = DefaultMaxBytes)
    {
        // Detect comma-separated list (has comma but no glob special chars)
        var isCommaSeparated = pattern.Contains(',')
            && !pattern.Contains('*')
            && !pattern.Contains('?')
            && !pattern.Contains('{');

        var errors = new List<string>();
        var results = new List<MultiGetResult>();

        if (isCommaSeparated)
        {
            var names = pattern.Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            foreach (var name in names)
            {
                var findResult = DocumentFinder.FindDocument(db, name, includeBody, similarFilesLimit: 3);
                if (findResult.IsFound)
                {
                    var doc = findResult.Document!;
                    if (doc.BodyLength > maxBytes)
                    {
                        results.Add(new MultiGetResult
                        {
                            Doc = new DocumentResult
                            {
                                Filepath = doc.Filepath,
                                DisplayPath = doc.DisplayPath,
                                Context = doc.Context,
                            },
                            Skipped = true,
                            SkipReason = $"File too large ({(int)Math.Round(doc.BodyLength / 1024.0)}KB > {maxBytes / 1024}KB)",
                        });
                    }
                    else
                    {
                        results.Add(new MultiGetResult { Doc = doc, Skipped = false });
                    }
                }
                else
                {
                    var similar = findResult.NotFound?.SimilarFiles ?? [];
                    var msg = $"File not found: {name}";
                    if (similar.Count > 0)
                        msg += $" (did you mean: {string.Join(", ", similar)}?)";
                    errors.Add(msg);
                }
            }
        }
        else
        {
            // Glob pattern match
            var matched = GlobMatcher.MatchFilesByGlob(db, pattern);
            if (matched.Count == 0)
            {
                errors.Add($"No files matched pattern: {pattern}");
                return ([], errors);
            }

            // Fetch full document info for matched files
            foreach (var match in matched)
            {
                if (match.BodyLength > maxBytes)
                {
                    results.Add(new MultiGetResult
                    {
                        Doc = new DocumentResult
                        {
                            Filepath = match.VirtualPath,
                            DisplayPath = match.DisplayPath,
                            Context = ContextResolver.GetContextForFile(db, match.VirtualPath),
                        },
                        Skipped = true,
                        SkipReason = $"File too large ({(int)Math.Round(match.BodyLength / 1024.0)}KB > {maxBytes / 1024}KB)",
                    });
                    continue;
                }

                var findResult = DocumentFinder.FindDocument(db, match.VirtualPath, includeBody);
                if (findResult.IsFound)
                {
                    results.Add(new MultiGetResult { Doc = findResult.Document!, Skipped = false });
                }
            }
        }

        return (results, errors);
    }
}
