using System.Text;
using System.Text.Json;
using Qmd.Core.Models;
using Qmd.Core.Snippets;

namespace Qmd.Core.Formatting;

public static class SearchResultFormatter
{
    public static string Format(List<SearchResult> results, OutputFormat format, FormatOptions? opts = null)
    {
        return format switch
        {
            OutputFormat.Json => ToJson(results, opts),
            OutputFormat.Csv => ToCsv(results, opts),
            OutputFormat.Files => ToFiles(results),
            OutputFormat.Cli => ToCli(results, opts),
            OutputFormat.Md => ToMarkdown(results, opts),
            OutputFormat.Xml => ToXml(results, opts),
            _ => ToJson(results, opts),
        };
    }

    public static string ToJson(List<SearchResult> results, FormatOptions? opts = null)
    {
        opts ??= new FormatOptions();
        var query = opts.Query ?? "";
        var items = results.Select(r =>
        {
            var bodyStr = r.Body ?? "";
            var snippetInfo = !string.IsNullOrEmpty(bodyStr)
                ? SnippetExtractor.ExtractSnippet(bodyStr, query, 300, r.ChunkPos, intent: opts.Intent)
                : null;

            var obj = new Dictionary<string, object?>
            {
                ["docid"] = $"#{r.DocId}",
                ["score"] = Math.Round(r.Score * 100) / 100,
                ["file"] = r.DisplayPath,
            };
            if (snippetInfo != null) obj["line"] = snippetInfo.Line;
            obj["title"] = r.Title;
            if (r.Context != null) obj["context"] = r.Context;
            if (opts.Full && r.Body != null)
            {
                obj["body"] = opts.LineNumbers ? FormatHelpers.AddLineNumbers(r.Body) : r.Body;
            }
            else if (snippetInfo != null)
            {
                var snippet = snippetInfo.Snippet;
                if (opts.LineNumbers) snippet = FormatHelpers.AddLineNumbers(snippet, snippetInfo.Line);
                obj["snippet"] = snippet;
            }
            if (opts.Explain && r.Explain != null)
            {
                obj["explain"] = BuildExplainDict(r.Explain);
            }
            return obj;
        }).ToList();

        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string ToCsv(List<SearchResult> results, FormatOptions? opts = null)
    {
        opts ??= new FormatOptions();
        var query = opts.Query ?? "";
        var sb = new StringBuilder();
        sb.AppendLine("docid,score,file,title,context,line,snippet");
        foreach (var r in results)
        {
            var bodyStr = r.Body ?? "";
            var snippetInfo = SnippetExtractor.ExtractSnippet(bodyStr, query, 500, r.ChunkPos, intent: opts.Intent);
            var content = opts.Full ? bodyStr : snippetInfo.Snippet;
            if (opts.LineNumbers && !string.IsNullOrEmpty(content))
                content = FormatHelpers.AddLineNumbers(content, snippetInfo.Line);

            sb.AppendLine(string.Join(',',
                FormatHelpers.EscapeCsv($"#{r.DocId}"),
                r.Score.ToString("F4"),
                FormatHelpers.EscapeCsv(r.DisplayPath),
                FormatHelpers.EscapeCsv(r.Title),
                FormatHelpers.EscapeCsv(r.Context),
                snippetInfo.Line,
                FormatHelpers.EscapeCsv(content)
            ));
        }
        return sb.ToString();
    }

    public static string ToFiles(List<SearchResult> results)
    {
        var sb = new StringBuilder();
        foreach (var r in results)
        {
            var ctx = r.Context != null ? $",\"{r.Context.Replace("\"", "\"\"")}\"" : "";
            sb.AppendLine($"#{r.DocId},{r.Score:F2},{r.DisplayPath}{ctx}");
        }
        return sb.ToString();
    }

    public static string ToCli(List<SearchResult> results, FormatOptions? opts = null)
    {
        opts ??= new FormatOptions();
        var query = opts.Query ?? "";
        var sb = new StringBuilder();

        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var snippet = !string.IsNullOrEmpty(r.Body)
                ? SnippetExtractor.ExtractSnippet(r.Body, query, 500, r.ChunkPos, intent: opts.Intent)
                : null;

            // Line 1: filepath with line number and docid
            var displayPath = r.DisplayPath;

            // Only show :line if a query term actually matches in the snippet body
            var lineInfo = "";
            if (snippet != null)
            {
                var snippetBody = string.Join("\n", snippet.Snippet.Split('\n').Skip(1)).ToLowerInvariant();
                var hasMatch = query.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Any(t => t.Length > 0 && snippetBody.Contains(t));
                if (hasMatch) lineInfo = $":{snippet.Line}";
            }

            var docidStr = !string.IsNullOrEmpty(r.DocId)
                ? $" {FormatHelpers.Dim}#{r.DocId}{FormatHelpers.Reset}"
                : "";

            var pathDisplay = $"{displayPath}{lineInfo}";
            if (!string.IsNullOrEmpty(opts.EditorUri) && !string.IsNullOrEmpty(r.Filepath))
            {
                var linkLine = snippet != null && !string.IsNullOrEmpty(lineInfo) ? snippet.Line : 1;
                pathDisplay = FormatHelpers.MakeTerminalLink(pathDisplay, opts.EditorUri, r.Filepath, linkLine);
            }

            sb.AppendLine($"{FormatHelpers.Cyan}{pathDisplay}{FormatHelpers.Reset}{docidStr}");

            // Line 2: Title
            if (!string.IsNullOrEmpty(r.Title))
                sb.AppendLine($"{FormatHelpers.Bold}Title: {r.Title}{FormatHelpers.Reset}");

            // Line 3: Context
            if (r.Context != null)
                sb.AppendLine($"{FormatHelpers.Dim}Context: {r.Context}{FormatHelpers.Reset}");

            // Line 4: Score
            var score = FormatHelpers.FormatScore(r.Score);
            sb.AppendLine($"Score: {FormatHelpers.Bold}{score}{FormatHelpers.Reset}");

            // Explain (if requested)
            if (opts.Explain && r.Explain != null)
                sb.Append(FormatExplainCli(r.Explain, dim: true));

            sb.AppendLine();

            // Snippet with term highlighting
            if (snippet != null)
            {
                var displaySnippet = opts.LineNumbers
                    ? FormatHelpers.AddLineNumbers(snippet.Snippet, snippet.Line)
                    : snippet.Snippet;
                sb.AppendLine(FormatHelpers.HighlightTerms(displaySnippet, query));
            }

            // Double blank line between results
            if (i < results.Count - 1)
                sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string ToMarkdown(List<SearchResult> results, FormatOptions? opts = null)
    {
        opts ??= new FormatOptions();
        var query = opts.Query ?? "";
        var sb = new StringBuilder();
        foreach (var r in results)
        {
            var heading = !string.IsNullOrEmpty(r.Title) ? r.Title : r.DisplayPath;
            sb.AppendLine("---");
            sb.AppendLine($"# {heading}");
            sb.AppendLine();
            sb.AppendLine($"**docid:** `#{r.DocId}`");
            if (r.Context != null) sb.AppendLine($"**context:** {r.Context}");
            sb.AppendLine();
            if (opts.Full && r.Body != null)
            {
                sb.AppendLine(opts.LineNumbers ? FormatHelpers.AddLineNumbers(r.Body) : r.Body);
            }
            else if (r.Body != null)
            {
                var snippet = SnippetExtractor.ExtractSnippet(r.Body, query, 500, r.ChunkPos, intent: opts.Intent);
                var content = snippet.Snippet;
                if (opts.LineNumbers) content = FormatHelpers.AddLineNumbers(content);
                sb.AppendLine(content);
            }
            if (opts.Explain && r.Explain != null)
            {
                sb.AppendLine();
                sb.Append(FormatExplainCli(r.Explain));
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FormatExplainNumber(double n) => n.ToString("F4");

    private static Dictionary<string, object?> BuildExplainDict(HybridQueryExplain explain)
    {
        var dict = new Dictionary<string, object?>
        {
            ["ftsScores"] = explain.FtsScores,
            ["vectorScores"] = explain.VectorScores,
            ["rerankScore"] = Math.Round(explain.RerankScore * 10000) / 10000,
            ["blendedScore"] = Math.Round(explain.BlendedScore * 10000) / 10000,
        };
        if (explain.Rrf != null)
        {
            dict["rrf"] = new Dictionary<string, object?>
            {
                ["baseScore"] = Math.Round(explain.Rrf.BaseScore * 10000) / 10000,
                ["topRankBonus"] = Math.Round(explain.Rrf.TopRankBonus * 10000) / 10000,
                ["totalScore"] = Math.Round(explain.Rrf.TotalScore * 10000) / 10000,
                ["contributions"] = explain.Rrf.Contributions.Select(c => new Dictionary<string, object?>
                {
                    ["source"] = c.Source,
                    ["queryType"] = c.QueryType,
                    ["rank"] = c.Rank,
                    ["backendScore"] = Math.Round(c.BackendScore * 10000) / 10000,
                    ["rrfContribution"] = Math.Round(c.RrfContribution * 10000) / 10000,
                }).ToList(),
            };
        }
        return dict;
    }

    private static string FormatExplainCli(HybridQueryExplain explain, bool dim = false)
    {
        var d = dim ? FormatHelpers.Dim : "";
        var r = dim ? FormatHelpers.Reset : "";
        var sb = new StringBuilder();
        var fts = explain.FtsScores.Count > 0
            ? string.Join(", ", explain.FtsScores.Select(FormatExplainNumber))
            : "none";
        var vec = explain.VectorScores.Count > 0
            ? string.Join(", ", explain.VectorScores.Select(FormatExplainNumber))
            : "none";
        sb.AppendLine($"{d}Explain: fts=[{fts}] vec=[{vec}]{r}");

        if (explain.Rrf != null)
        {
            var rrf = explain.Rrf;
            sb.AppendLine($"{d}  RRF: total={FormatExplainNumber(rrf.TotalScore)} base={FormatExplainNumber(rrf.BaseScore)} bonus={FormatExplainNumber(rrf.TopRankBonus)} rank={rrf.TopRank}{r}");

            // Compute RRF weight: if rerank score > 0, the TS version uses the rrf positionScore weight
            var rrfWeight = explain.RerankScore > 0 ? 0.5 : 1.0;
            var rrfPos = rrf.TotalScore;
            sb.AppendLine($"{d}  Blend: {(int)(rrfWeight * 100)}%*{FormatExplainNumber(rrfPos)} + {(int)((1 - rrfWeight) * 100)}%*{FormatExplainNumber(explain.RerankScore)} = {FormatExplainNumber(explain.BlendedScore)}{r}");

            var topContribs = rrf.Contributions
                .OrderByDescending(c => c.RrfContribution)
                .Take(3)
                .Select(c => $"{c.Source}/{c.QueryType}#{c.Rank}:{FormatExplainNumber(c.RrfContribution)}");
            var contribSummary = string.Join(" | ", topContribs);
            if (!string.IsNullOrEmpty(contribSummary))
                sb.AppendLine($"{d}  Top RRF contributions: {contribSummary}{r}");
        }
        return sb.ToString();
    }

    public static string ToXml(List<SearchResult> results, FormatOptions? opts = null)
    {
        opts ??= new FormatOptions();
        var query = opts.Query ?? "";
        var items = results.Select(r =>
        {
            var titleAttr = !string.IsNullOrEmpty(r.Title) ? $" title=\"{FormatHelpers.EscapeXml(r.Title)}\"" : "";
            var contextAttr = r.Context != null ? $" context=\"{FormatHelpers.EscapeXml(r.Context)}\"" : "";
            var bodyStr = r.Body ?? "";
            var content = opts.Full ? bodyStr : SnippetExtractor.ExtractSnippet(bodyStr, query, 500, r.ChunkPos, intent: opts.Intent).Snippet;
            if (opts.LineNumbers) content = FormatHelpers.AddLineNumbers(content);
            return $"<file docid=\"#{r.DocId}\" name=\"{FormatHelpers.EscapeXml(r.DisplayPath)}\"{titleAttr}{contextAttr}>\n{FormatHelpers.EscapeXml(content)}\n</file>";
        });
        return string.Join("\n\n", items);
    }
}
