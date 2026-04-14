using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Qmd.Core.Content;
using Qmd.Core.Models;
using Qmd.Core.Snippets;

namespace Qmd.Core.Mcp;

/// <summary>
/// MCP tools for QMD. Discovered via [McpServerToolType] attribute.
/// </summary>
[McpServerToolType]
internal class QmdTools
{
    private static readonly JsonSerializerOptions McpJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IQmdStore _store;

    public QmdTools(IQmdStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Search the knowledge base using typed sub-queries.
    /// Each search has a type (lex for keywords, vec for semantic, hyde for hypothetical).
    /// The first query gets 2x weight in ranking.
    /// </summary>
    [McpServerTool(Name = "query")]
    [Description("Search the knowledge base using typed sub-queries. Each search has a type: lex (BM25 keyword — supports \"exact phrase\", -negation, prefix*), vec (semantic similarity), or hyde (hypothetical document that would answer the question). Use lex for exact terms/IDs, vec for concepts, hyde for questions. Combine 2-3 types for best results. The first query gets 2x weight.")]
    public async Task<CallToolResult> Query(
        [Description("Search query text (used when searches is omitted)")] string? query = null,
        [Description("JSON array of typed searches: [{\"type\":\"lex\",\"query\":\"...\"}, {\"type\":\"vec\",\"query\":\"...\"}]. Types: lex (keyword), vec (semantic), hyde (hypothetical doc).")] string? searches = null,
        [Description("Max results to return (default: 10)")] int limit = 10,
        [Description("Min relevance score 0-1 (default: 0)")] double minScore = 0,
        [Description("Filter to specific collection name(s), comma-separated")] string? collection = null,
        [Description("Domain context to disambiguate query (not searched)")] string? intent = null,
        [Description("Maximum candidates to rerank (default: 40)")] int? candidateLimit = null,
        [Description("Rerank results using LLM (default: true)")] bool rerank = true,
        CancellationToken ct = default)
    {
        var collections = collection?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        // Use default collections when none specified
        var collList = collections is { Count: > 0 }
            ? collections
            : await _store.GetDefaultCollectionNamesAsync();

        List<HybridQueryResult> results;
        string primaryQuery;

        // If structured searches provided, use StructuredSearchService
        if (searches != null)
        {
            var parsed = JsonSerializer.Deserialize<List<SearchEntry>>(searches, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (parsed is not { Count: > 0 })
                return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "Error: searches must be a non-empty JSON array of {type, query} objects." }] };

            var expandedQueries = parsed
                .Select(e => new ExpandedQuery(e.Type ?? "lex", e.Query ?? ""))
                .ToList();

            // Use first lex or vec query for snippet extraction
            primaryQuery = parsed.FirstOrDefault(s => s.Type == "lex")?.Query
                ?? parsed.FirstOrDefault(s => s.Type == "vec")?.Query
                ?? parsed[0].Query ?? "";

            results = await _store.SearchStructuredAsync(expandedQueries,
                new StructuredSearchOptions
                {
                    Collections = collList,
                    Limit = limit,
                    MinScore = minScore,
                    Intent = intent,
                    SkipRerank = !rerank,
                    CandidateLimit = candidateLimit ?? 40,
                }, ct);
        }
        else if (query != null)
        {
            primaryQuery = query;
            results = await _store.SearchAsync(new SearchOptions
            {
                Query = query,
                Limit = limit,
                MinScore = minScore,
                Collections = collList,
                Intent = intent,
                SkipRerank = !rerank,
            }, ct);
        }
        else
        {
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "Error: provide either 'query' (plain text) or 'searches' (typed sub-queries)." }] };
        }

        if (results.Count == 0)
            return new CallToolResult { Content = [new TextContentBlock { Text = "No results found." }] };

        // Build structured results with snippets
        var structured = results.Select(r =>
        {
            var snippet = SnippetExtractor.ExtractSnippet(
                r.BestChunk ?? r.Body, primaryQuery, 300, r.BestChunkPos, intent: intent);
            return new
            {
                docid = $"#{r.Docid}",
                file = r.DisplayPath,
                title = r.Title,
                score = Math.Round(r.Score * 100) / 100,
                context = r.Context,
                snippet = TextUtils.AddLineNumbers(snippet.Snippet, snippet.Line),
            };
        }).ToList();

        // Build text summary
        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Count} result(s) for \"{primaryQuery}\":");
        foreach (var r in structured)
        {
            var pct = (int)(r.score * 100);
            sb.AppendLine($"{r.docid} {pct}% {r.file} - {r.title}");
        }

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = sb.ToString() }],
            StructuredContent = JsonDocument.Parse(
                JsonSerializer.Serialize(new { results = structured }, McpJsonOpts)).RootElement,
        };
    }

    private class SearchEntry
    {
        public string? Type { get; set; }
        public string? Query { get; set; }
    }

    /// <summary>
    /// Encode path segments for URI, preserving "/" separators.
    /// </summary>
    private static string EncodeQmdPath(string path) =>
        string.Join("/", path.Split('/').Select(Uri.EscapeDataString));

    /// <summary>
    /// Retrieve the full content of a document by file path or docid.
    /// </summary>
    [McpServerTool(Name = "get")]
    [Description("Retrieve a document by file path or docid (#abc123)")]
    public async Task<CallToolResult> Get(
        [Description("File path, virtual path (qmd://...), or docid (#abc123). Append :N for line offset.")] string file,
        [Description("Start from this line number (1-indexed)")] int? fromLine = null,
        [Description("Maximum lines to return")] int? maxLines = null,
        [Description("Add line numbers to output")] bool lineNumbers = false)
    {
        // Support :line suffix in file (e.g. "foo.md:120") when fromLine isn't provided
        var parsedFromLine = fromLine;
        var lookup = file;
        var colonMatch = System.Text.RegularExpressions.Regex.Match(lookup, @":(\d+)$");
        if (colonMatch.Success && parsedFromLine == null)
        {
            parsedFromLine = int.Parse(colonMatch.Groups[1].Value);
            lookup = lookup[..^colonMatch.Length];
        }

        var result = await _store.GetAsync(lookup, new GetOptions { IncludeBody = false });

        if (!result.IsFound)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Document not found: {file}");
            if (result.NotFound!.SimilarFiles.Count > 0)
            {
                sb.AppendLine("\n\nDid you mean one of these?");
                foreach (var f in result.NotFound.SimilarFiles)
                    sb.AppendLine($"  - {f}");
            }
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = sb.ToString() }] };
        }

        var doc = result.Document!;
        var body = await _store.GetDocumentBodyAsync(doc.Filepath,
            new BodyOptions { FromLine = parsedFromLine, MaxLines = maxLines }) ?? "";

        if (lineNumbers)
            body = TextUtils.AddLineNumbers(body, parsedFromLine ?? 1);

        var text = body;
        if (doc.Context != null)
            text = $"<!-- Context: {doc.Context} -->\n\n" + text;

        var uri = "qmd://" + EncodeQmdPath(doc.DisplayPath);

        return new CallToolResult
        {
            Content = [new EmbeddedResourceBlock
            {
                Resource = new TextResourceContents
                {
                    Uri = uri,
                    MimeType = "text/markdown",
                    Text = text,
                },
            }],
        };
    }

    /// <summary>
    /// Retrieve multiple documents by glob pattern or comma-separated list.
    /// </summary>
    [McpServerTool(Name = "multi_get")]
    [Description("Retrieve multiple documents by glob pattern or comma-separated file list")]
    public async Task<CallToolResult> MultiGet(
        [Description("Glob pattern (e.g., 'docs/*.md') or comma-separated list")] string pattern,
        [Description("Maximum lines per file")] int? maxLines = null,
        [Description("Skip files larger than this (bytes, default: 10240)")] int maxBytes = 10240,
        [Description("Add line numbers to output")] bool lineNumbers = false)
    {
        var (docs, errors) = await _store.MultiGetAsync(pattern, new MultiGetOptions
        {
            MaxBytes = maxBytes,
            IncludeBody = true,
        });

        if (docs.Count == 0 && errors.Count == 0)
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = $"No files matched pattern: {pattern}" }] };

        var content = new List<ContentBlock>();

        // Add errors as text block
        if (errors.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{errors.Count} error(s):");
            foreach (var err in errors) sb.AppendLine($"  - {err}");
            content.Add(new TextContentBlock { Text = sb.ToString() });
        }

        // Add each document as a resource block
        foreach (var item in docs)
        {
            if (item.Skipped)
            {
                content.Add(new TextContentBlock { Text = $"[SKIPPED: {item.Doc.DisplayPath} — {item.SkipReason}. Use get tool for large files.]" });
            }
            else
            {
                var body = item.Doc.Body ?? "";
                if (maxLines.HasValue)
                {
                    var lines = body.Split('\n');
                    if (lines.Length > maxLines.Value)
                    {
                        body = string.Join('\n', lines.Take(maxLines.Value));
                        body += $"\n[... truncated {lines.Length - maxLines.Value} more lines]";
                    }
                }
                if (lineNumbers) body = TextUtils.AddLineNumbers(body);

                // Prepend context comment if available
                if (item.Doc.Context != null)
                    body = $"<!-- Context: {item.Doc.Context} -->\n\n" + body;

                var uri = "qmd://" + EncodeQmdPath(item.Doc.DisplayPath);
                content.Add(new EmbeddedResourceBlock
                {
                    Resource = new TextResourceContents
                    {
                        Uri = uri,
                        MimeType = "text/markdown",
                        Text = body,
                    },
                });
            }
        }

        return new CallToolResult { Content = content };
    }

    /// <summary>
    /// Show QMD index status: document counts, collections, embedding state.
    /// </summary>
    [McpServerTool(Name = "status")]
    [Description("Show QMD index status including document counts, collections, and embedding state")]
    public async Task<CallToolResult> Status()
    {
        var status = await _store.GetStatusAsync();

        var sb = new StringBuilder();
        sb.AppendLine("QMD Index Status:");
        sb.AppendLine($"  Total documents: {status.TotalDocuments}");
        sb.AppendLine($"  Needs embedding: {status.NeedsEmbedding}");
        sb.AppendLine($"  Vector index: {(status.HasVectorIndex ? "yes" : "no")}");

        if (status.Collections.Count > 0)
        {
            sb.AppendLine($"\nCollections ({status.Collections.Count}):");
            foreach (var c in status.Collections)
                sb.AppendLine($"  {c.Name}: {c.Documents} docs ({c.Path})");
        }

        var structured = new
        {
            totalDocuments = status.TotalDocuments,
            needsEmbedding = status.NeedsEmbedding,
            hasVectorIndex = status.HasVectorIndex,
            collections = status.Collections.Select(c => new
            {
                name = c.Name,
                path = c.Path,
                pattern = c.Pattern,
                documents = c.Documents,
                lastUpdated = c.LastUpdated,
            }).ToList(),
        };

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = sb.ToString() }],
            StructuredContent = JsonDocument.Parse(
                JsonSerializer.Serialize(structured, McpJsonOpts)).RootElement,
        };
    }
}
