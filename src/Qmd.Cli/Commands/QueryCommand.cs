using System.CommandLine;
using Qmd.Core.Formatting;
using Qmd.Core.Models;
using Qmd.Sdk;
using Spectre.Console;

namespace Qmd.Cli.Commands;

public static class QueryCommand
{
    public static Command Create()
    {
        var queryArg = new Argument<string>("query") { Description = "Search query" };
        var limitOpt = new Option<int>("--limit", "-n") { Description = "Max results", DefaultValueFactory = _ => 10 };
        var collectionOpt = new Option<string[]>("--collection", "-c") { Description = "Filter by collection(s)", AllowMultipleArgumentsPerToken = true };
        var intentOpt = new Option<string?>("--intent") { Description = "Domain context for search" };
        var noRerankOpt = new Option<bool>("--no-rerank") { Description = "Skip LLM reranking" };
        var minScoreOpt = new Option<double>("--min-score") { Description = "Minimum relevance score", DefaultValueFactory = _ => 0.2 };
        var allOpt = new Option<bool>("--all") { Description = "Return all results" };
        var candidateLimitOpt = new Option<int>("--candidate-limit", "-C") { Description = "Max reranking candidates", DefaultValueFactory = _ => 40 };
        var chunkStrategyOpt = new Option<string>("--chunk-strategy") { Description = "Chunking strategy: regex or auto", DefaultValueFactory = _ => "regex" };
        var formatOpt = new Option<string>("--format") { Description = "Output format", DefaultValueFactory = _ => "cli" };
        var fullOpt = new Option<bool>("--full") { Description = "Show full document content" };
        var lineNumbersOpt = new Option<bool>("--line-numbers") { Description = "Add line numbers" };
        var explainOpt = new Option<bool>("--explain") { Description = "Show retrieval traces" };
        var (jsonOpt, csvOpt, mdOpt, xmlOpt, filesOpt) = CliHelper.CreateFormatAliasOptions();

        var cmd = new Command("query", "Hybrid search with query expansion and reranking (recommended)")
        {
            queryArg, limitOpt, collectionOpt, intentOpt, noRerankOpt, minScoreOpt,
            allOpt, candidateLimitOpt, chunkStrategyOpt, formatOpt, fullOpt, lineNumbersOpt, explainOpt,
            jsonOpt, csvOpt, mdOpt, xmlOpt, filesOpt
        };
        cmd.Aliases.Add("deep-search");

        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var query = parseResult.GetValue(queryArg) ?? throw new InvalidOperationException("Required argument 'query' was not provided.");
            var collections = parseResult.GetValue(collectionOpt) ?? [];
            var intent = parseResult.GetValue(intentOpt);
            var noRerank = parseResult.GetValue(noRerankOpt);
            var minScore = parseResult.GetValue(minScoreOpt);
            var all = parseResult.GetValue(allOpt);
            var candidateLimit = parseResult.GetValue(candidateLimitOpt);
            var chunkStrategy = parseResult.GetValue(chunkStrategyOpt);
            var format = parseResult.GetValue(formatOpt) ?? "cli";
            var full = parseResult.GetValue(fullOpt);
            var lineNumbers = parseResult.GetValue(lineNumbersOpt);
            var explain = parseResult.GetValue(explainOpt);

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

            var strategy = chunkStrategy?.ToLowerInvariant() == "auto"
                ? ChunkStrategy.Auto : ChunkStrategy.Regex;

            // Check for structured query syntax (lex:/vec:/hyde: prefixes)
            var structured = CliHelper.ParseStructuredQuery(query);

            await using var store = await CliHelper.CreateStoreAsync();
            var collList = await CliHelper.ResolveCollectionsAsync(store, collections);

            var stderr = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(Console.Error),
            });
            var diagnostics = new HybridQueryDiagnostics();
            List<HybridQueryResult> results = [];
            await stderr.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Searching...", async _ =>
                {
                    if (structured != null)
                    {
                        results = await store.SearchStructuredAsync(structured.Queries,
                            new StructuredSearchOptions
                            {
                                Collections = collList,
                                Limit = limit,
                                MinScore = minScore,
                                CandidateLimit = candidateLimit,
                                Intent = structured.Intent ?? intent,
                                SkipRerank = noRerank,
                                ChunkStrategy = strategy,
                                Explain = explain,
                            });
                    }
                    else
                    {
                        results = await store.SearchAsync(new SearchOptions
                        {
                            Query = query,
                            Limit = limit,
                            Collections = collList,
                            Intent = intent,
                            SkipRerank = noRerank,
                            CandidateLimit = candidateLimit,
                            ChunkStrategy = strategy,
                            Explain = explain,
                            Diagnostics = diagnostics,
                        });
                    }
                });

            if (diagnostics is { HasFtsResults: false } && results.Count > 0)
                Console.Error.WriteLine("Note: no keyword matches found -- results are based on semantic similarity only and may be less precise.");

            if (minScore > 0)
                results = results.Where(r => r.Score >= minScore).ToList();

            if (results.Count == 0)
            {
                CliHelper.PrintEmptySearchResults(outputFormat, minScore > 0
                    ? $"No results above --min-score {minScore}."
                    : null);
                return;
            }

            var searchResults = results.Select(r => new SearchResult
            {
                Filepath = r.File,
                DisplayPath = r.DisplayPath,
                Title = r.Title,
                Hash = "",
                DocId = r.Docid,
                Body = full ? r.Body : r.BestChunk,
                Score = r.Score,
                Source = "hybrid",
                Context = r.Context,
                ChunkPos = r.BestChunkPos,
                Explain = r.Explain,
            }).ToList();

            var output = SearchResultFormatter.Format(searchResults, outputFormat,
                new FormatOptions { Full = full, Query = query, LineNumbers = lineNumbers, Intent = intent, Explain = explain });
            Console.Write(output);
        });

        return cmd;
    }
}
