using System.CommandLine;
using Qmd.Core;
using Qmd.Cli.Formatting;
using Qmd.Core.Models;

namespace Qmd.Cli.Commands;

public static class SearchCommand
{
    public static Command Create()
    {
        var queryArg = new Argument<string>("query") { Description = "Search query" };
        var limitOpt = new Option<int>("--limit", "-n") { Description = "Max results", DefaultValueFactory = _ => 5 };
        var collectionOpt = new Option<string[]>("--collection", "-c") { Description = "Filter by collection(s)", AllowMultipleArgumentsPerToken = true };
        var minScoreOpt = new Option<double>("--min-score") { Description = "Minimum BM25 relevance score", DefaultValueFactory = _ => 0 };
        var allOpt = new Option<bool>("--all") { Description = "Return all results" };
        var formatOpt = new Option<string>("--format") { Description = "Output format: cli, json, csv, md, xml, files", DefaultValueFactory = _ => "cli" };
        var fullOpt = new Option<bool>("--full") { Description = "Show full document content" };
        var lineNumbersOpt = new Option<bool>("--line-numbers") { Description = "Add line numbers" };
        var (jsonOpt, csvOpt, mdOpt, xmlOpt, filesOpt) = CliHelper.CreateFormatAliasOptions();

        var cmd = new Command("search", "BM25 full-text keyword search")
        {
            queryArg, limitOpt, collectionOpt, minScoreOpt, allOpt, formatOpt, fullOpt, lineNumbersOpt,
            jsonOpt, csvOpt, mdOpt, xmlOpt, filesOpt
        };

        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var query = parseResult.GetValue(queryArg) ?? throw new InvalidOperationException("Required argument 'query' was not provided.");
            var collections = parseResult.GetValue(collectionOpt) ?? [];
            var minScore = parseResult.GetValue(minScoreOpt);
            var all = parseResult.GetValue(allOpt);
            var format = parseResult.GetValue(formatOpt) ?? "cli";
            var full = parseResult.GetValue(fullOpt);
            var lineNumbers = parseResult.GetValue(lineNumbersOpt);

            var outputFormat = CliHelper.ResolveFormat(format,
                parseResult.GetValue(jsonOpt),
                parseResult.GetValue(csvOpt),
                parseResult.GetValue(mdOpt),
                parseResult.GetValue(xmlOpt),
                parseResult.GetValue(filesOpt));

            // Format-aware default limit (5 for CLI, 20 for json/files)
            var limit = parseResult.GetValue(limitOpt);
            if (limit == 5 && parseResult.GetResult(limitOpt) is null)
                limit = CliHelper.DefaultLimitForFormat(outputFormat);
            if (all) limit = 100000;

            await using var store = await CliHelper.CreateStoreAsync();
            await HandleSearchAsync(store, query, collections, limit, minScore, outputFormat, full, lineNumbers);
        });

        return cmd;
    }

    internal static async Task HandleSearchAsync(
        IQmdStore store, string query, string[] collections, int limit,
        double minScore, OutputFormat outputFormat, bool full, bool lineNumbers)
    {
        var collList = await CliHelper.ResolveCollectionsAsync(store, collections);
        var results = await store.SearchLexAsync(query, new LexSearchOptions
        {
            Limit = limit,
            Collections = collList,
        });

        if (minScore > 0)
            results = results.Where(r => r.Score >= minScore).ToList();

        if (results.Count == 0)
        {
            CliHelper.PrintEmptySearchResults(outputFormat, minScore > 0
                ? $"No results above --min-score {minScore}."
                : null);
            return;
        }

        var output = SearchResultFormatter.Format(results, outputFormat,
            new FormatOptions { Full = full, Query = query, LineNumbers = lineNumbers });
        CliContext.Console.Write(output);
    }
}
