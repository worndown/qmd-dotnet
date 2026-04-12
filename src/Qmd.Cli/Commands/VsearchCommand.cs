using System.CommandLine;
using Qmd.Core;
using Qmd.Cli.Formatting;
using Qmd.Core.Models;
using Spectre.Console;

namespace Qmd.Cli.Commands;

public static class VsearchCommand
{
    public static Command Create()
    {
        var queryArg = new Argument<string>("query") { Description = "Search query" };
        var limitOpt = new Option<int>("--limit", "-n") { Description = "Max results", DefaultValueFactory = _ => 10 };
        var collectionOpt = new Option<string[]>("--collection", "-c") { Description = "Filter by collection(s)", AllowMultipleArgumentsPerToken = true };
        var minScoreOpt = new Option<double>("--min-score") { Description = "Minimum cosine similarity score", DefaultValueFactory = _ => 0.5 };
        var allOpt = new Option<bool>("--all") { Description = "Return all results" };
        var intentOpt = new Option<string?>("--intent") { Description = "Domain context for search" };
        var formatOpt = new Option<string>("--format") { Description = "Output format: cli, json, csv, md, xml, files", DefaultValueFactory = _ => "cli" };
        var fullOpt = new Option<bool>("--full") { Description = "Show full document content" };
        var lineNumbersOpt = new Option<bool>("--line-numbers") { Description = "Add line numbers" };
        var (jsonOpt, csvOpt, mdOpt, xmlOpt, filesOpt) = CliHelper.CreateFormatAliasOptions();

        var cmd = new Command("vsearch", "Vector similarity search (no reranking)")
        {
            queryArg, limitOpt, collectionOpt, minScoreOpt, allOpt, intentOpt,
            formatOpt, fullOpt, lineNumbersOpt,
            jsonOpt, csvOpt, mdOpt, xmlOpt, filesOpt
        };
        cmd.Aliases.Add("vector-search");

        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var query = parseResult.GetValue(queryArg) ?? throw new InvalidOperationException("Required argument 'query' was not provided.");
            var collections = parseResult.GetValue(collectionOpt) ?? [];
            var minScore = parseResult.GetValue(minScoreOpt);
            var all = parseResult.GetValue(allOpt);
            var intent = parseResult.GetValue(intentOpt);
            var format = parseResult.GetValue(formatOpt) ?? "cli";
            var full = parseResult.GetValue(fullOpt);
            var lineNumbers = parseResult.GetValue(lineNumbersOpt);

            var outputFormat = CliHelper.ResolveFormat(format,
                parseResult.GetValue(jsonOpt),
                parseResult.GetValue(csvOpt),
                parseResult.GetValue(mdOpt),
                parseResult.GetValue(xmlOpt),
                parseResult.GetValue(filesOpt));

            var limit = parseResult.GetValue(limitOpt);
            if (limit == 10 && parseResult.GetResult(limitOpt) is null)
                limit = CliHelper.DefaultLimitForFormat(outputFormat);
            if (all) limit = 100000;

            await using var store = await CliHelper.CreateStoreAsync();
            var collList = await CliHelper.ResolveCollectionsAsync(store, collections);
            var stderr = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(Console.Error),
            });
            List<SearchResult> results = [];
            await stderr.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Searching...", async _ =>
                {
                    results = await store.SearchVectorAsync(query, new VectorSearchOptions
                    {
                        Limit = limit,
                        MinScore = minScore,
                        Collections = collList,
                        Intent = intent,
                    });
                });

            if (results.Count == 0)
            {
                CliHelper.PrintEmptySearchResults(outputFormat, minScore > 0.5
                    ? $"No results above --min-score {minScore}. Note: cosine similarity scores differ from BM25 scores; try a lower threshold."
                    : null);
                return;
            }

            var output = SearchResultFormatter.Format(results, outputFormat,
                new FormatOptions { Full = full, Query = query, LineNumbers = lineNumbers, Intent = intent });
            Console.Write(output);
        });

        return cmd;
    }
}
