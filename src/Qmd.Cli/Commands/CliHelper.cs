using System.CommandLine;
using Qmd.Cli.Formatting;
using Qmd.Core;
using Qmd.Core.Llm;
using Qmd.Core.Models;
using Qmd.Core.Paths;

namespace Qmd.Cli.Commands;

/// <summary>
/// Shared CLI helper for store lifecycle and option parsing.
/// </summary>
internal static class CliHelper
{
    /// <summary>
    /// Optional override for database index name (set by --index global option).
    /// </summary>
    public static string? IndexName { get; set; }

    /// <summary>
    /// Optional factory override for testing. When set, CreateStoreAsync uses this instead of the filesystem path.
    /// </summary>
    internal static Func<Task<IQmdStore>>? StoreFactory { get; set; }

    public static async Task<IQmdStore> CreateStoreAsync()
    {
        if (StoreFactory != null) return await StoreFactory();
        QmdPaths.EnableProductionMode();
        var dbPath = IndexName != null
            ? QmdPaths.GetDefaultDbPath(IndexName)
            : QmdPaths.GetDefaultDbPath();
        return await QmdStoreFactory.CreateAsync(new StoreOptions { DbPath = dbPath, LlmService = LlmServiceFactory.Create() });
    }

    public static OutputFormat ParseFormat(string format) => format.ToLowerInvariant() switch
    {
        "json" => OutputFormat.Json,
        "csv" => OutputFormat.Csv,
        "md" or "markdown" => OutputFormat.Md,
        "xml" => OutputFormat.Xml,
        "files" => OutputFormat.Files,
        _ => OutputFormat.Cli,
    };

    /// <summary>
    /// Resolve collection filter: if user passed -c flags, use those;
    /// otherwise default to collections with includeByDefault != false.
    /// </summary>
    public static async Task<List<string>?> ResolveCollectionsAsync(IQmdStore store, string[] cliCollections)
    {
        if (cliCollections.Length > 0)
            return cliCollections.ToList();

        var defaults = await store.GetDefaultCollectionNamesAsync();
        return defaults.Count > 0 ? defaults : null;
    }

    /// <summary>
    /// Add --json, --csv, --md, --xml, --files as boolean aliases for a --format option.
    /// When any alias is set, it overrides the format option value.
    /// These are implemented as separate options whose values are merged in the handler.
    /// We use Option.AddAlias for common shortcut patterns instead.
    /// </summary>
    /// <summary>
    /// Add --json, --csv, --md, --xml, --files boolean options to a command.
    /// Returns a function that resolves the effective format from the invocation.
    /// </summary>
    public static (Option<bool> Json, Option<bool> Csv, Option<bool> Md, Option<bool> Xml, Option<bool> Files) CreateFormatAliasOptions()
    {
        return (
            new Option<bool>("--json") { Description = "Output as JSON" },
            new Option<bool>("--csv") { Description = "Output as CSV" },
            new Option<bool>("--md") { Description = "Output as Markdown" },
            new Option<bool>("--xml") { Description = "Output as XML" },
            new Option<bool>("--files") { Description = "Output as file list" }
        );
    }

    /// <summary>
    /// Resolve the output format from --format string and boolean shortcut flags.
    /// Boolean flags take precedence over --format.
    /// </summary>
    public static OutputFormat ResolveFormat(string format, bool json, bool csv, bool md, bool xml, bool files)
    {
        if (json) return OutputFormat.Json;
        if (csv) return OutputFormat.Csv;
        if (md) return OutputFormat.Md;
        if (xml) return OutputFormat.Xml;
        if (files) return OutputFormat.Files;
        return ParseFormat(format);
    }

    /// <summary>
    /// Determine default result limit based on output format.
    /// 5 for CLI, 20 for --json and --files.
    /// </summary>
    public static int DefaultLimitForFormat(OutputFormat format) =>
        format is OutputFormat.Json or OutputFormat.Files ? 20 : 5;

    /// <summary>
    /// Print format-appropriate empty output for search commands.
    /// </summary>
    public static void PrintEmptySearchResults(OutputFormat format, string? scoreHint = null)
    {
        switch (format)
        {
            case OutputFormat.Json:
                CliContext.Console.Write("[]");
                return;
            case OutputFormat.Csv:
                CliContext.Console.Write("docid,score,file,title,context,line,snippet");
                return;
            case OutputFormat.Xml:
                CliContext.Console.Write("<results></results>");
                return;
            case OutputFormat.Md:
            case OutputFormat.Files:
                return;
        }

        // CLI format: show a helpful message
        if (scoreHint != null)
            CliContext.Console.WriteErrorLine(scoreHint);
        else
            CliContext.Console.WriteErrorLine("No results found.");
    }

    /// <summary>
    /// Parse structured query syntax: lines prefixed with lex:, vec:, hyde:, intent:
    /// Returns null if the query doesn't contain structured prefixes.
    /// </summary>
    public static ParsedStructuredQuery? ParseStructuredQuery(string query)
    {
        // Build list of non-empty lines, tracking original 1-indexed line numbers
        var rawLines = query.Split('\n')
            .Select((line, idx) => (Raw: line, Trimmed: line.Trim(), Number: idx + 1))
            .Where(l => l.Trimmed.Length > 0)
            .ToList();

        if (rawLines.Count == 0) return null;

        var prefixRe = new System.Text.RegularExpressions.Regex(@"^(lex|vec|hyde):\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var expandRe = new System.Text.RegularExpressions.Regex(@"^expand:\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var intentRe = new System.Text.RegularExpressions.Regex(@"^intent:\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var queries = new List<ExpandedQuery>();
        string? intent = null;

        foreach (var line in rawLines)
        {
            // Check for expand: prefix
            if (expandRe.IsMatch(line.Trimmed))
            {
                if (rawLines.Count > 1)
                    throw new ArgumentException($"Line {line.Number} starts with expand:, but query documents cannot mix expand with typed lines. Submit a single expand query instead.");
                var expandText = expandRe.Replace(line.Trimmed, "").Trim();
                if (string.IsNullOrEmpty(expandText))
                    throw new ArgumentException("expand: query must include text.");
                return null;
            }

            // Check for intent: prefix
            if (intentRe.IsMatch(line.Trimmed))
            {
                if (intent != null)
                    throw new ArgumentException($"Line {line.Number}: only one intent: line is allowed per query document.");
                intent = intentRe.Replace(line.Trimmed, "").Trim();
                if (string.IsNullOrEmpty(intent))
                    throw new ArgumentException("intent: must include text.");
                continue;
            }

            // Check for typed prefix (lex/vec/hyde)
            var match = prefixRe.Match(line.Trimmed);
            if (match.Success)
            {
                var type = match.Groups[1].Value.ToLowerInvariant();
                var text = line.Trimmed[match.Length..].Trim();
                if (string.IsNullOrEmpty(text))
                    throw new ArgumentException($"Line {line.Number} ({type}:) must include text.");
                queries.Add(new ExpandedQuery(type, text, line.Number));
                continue;
            }

            // Plain line (no prefix)
            if (rawLines.Count == 1)
                return null;

            throw new ArgumentException($"Line {line.Number} is missing a lex:/vec:/hyde: prefix. Each line in a query document must start with one.");
        }

        if (intent != null && queries.Count == 0)
            throw new ArgumentException("intent: cannot appear alone — add at least one lex:, vec:, or hyde: query.");

        return queries.Count > 0 ? new ParsedStructuredQuery(queries, intent) : null;
    }
}
