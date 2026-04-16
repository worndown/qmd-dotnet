using System.CommandLine;
using System.Text.Json;
using Qmd.Core.Bench;
using Qmd.Core.Models;
using Qmd.Core.Search;
using Spectre.Console;

namespace Qmd.Cli.Commands;

public static class AutotuneCommand
{
    public static Command Create()
    {
        var fixtureOpt = new Option<string?>("--fixture", "-f") { Description = "Path to benchmark fixture JSON for grid search" };
        var dryRunOpt = new Option<bool>("--dry-run") { Description = "Print recommendation without saving" };
        var resetOpt = new Option<bool>("--reset") { Description = "Remove saved config and revert to defaults" };
        var sampleSizeOpt = new Option<int>("--sample-size", "-n") { Description = "Sample size for embedding profiling", DefaultValueFactory = _ => 100 };
        var collectionOpt = new Option<string[]>("--collection", "-c") { Description = "Filter by collection(s)", AllowMultipleArgumentsPerToken = true };

        var cmd = new Command("autotune", "Auto-tune search thresholds using embedding profile or benchmark grid search")
        {
            fixtureOpt, dryRunOpt, resetOpt, sampleSizeOpt, collectionOpt
        };

        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var fixturePath = parseResult.GetValue(fixtureOpt);
            var dryRun = parseResult.GetValue(dryRunOpt);
            var reset = parseResult.GetValue(resetOpt);
            var sampleSize = parseResult.GetValue(sampleSizeOpt);
            var collections = parseResult.GetValue(collectionOpt) ?? [];

            await using var store = await CliHelper.CreateStoreAsync();

            // --reset: clear saved config and return
            if (reset)
            {
                await store.ResetSearchConfigAsync();
                CliContext.Console.WriteLine("Search config reset to defaults.");
                return 0;
            }

            var collList = collections.Length > 0
                ? await CliHelper.ResolveCollectionsAsync(store, collections)
                : null;

            var profileOptions = new EmbeddingProfileOptions
            {
                SampleSize = sampleSize,
                Collections = collList,
            };

            var stderr = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(Console.Error),
            });

            AutotuneResult result;

            if (fixturePath != null)
            {
                // Bench-based autotune with grid search
                var resolvedPath = Path.GetFullPath(fixturePath);
                if (!File.Exists(resolvedPath))
                {
                    CliContext.Console.WriteErrorLine($"Fixture file not found: {resolvedPath}");
                    return 1;
                }

                var raw = await File.ReadAllTextAsync(resolvedPath, token);
                var fixture = JsonSerializer.Deserialize<BenchmarkFixture>(raw);
                if (fixture?.Queries is not { Count: > 0 })
                {
                    CliContext.Console.WriteErrorLine("Invalid fixture: missing 'queries' array");
                    return 1;
                }

                var collection = collections.Length > 0 ? collections[0] : null;

                result = await stderr.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Running autotune grid search...", async _ =>
                    {
                        return await AutotuneRunner.BenchBasedAutotuneAsync(
                            store, fixture, collection, profileOptions, token);
                    });
            }
            else
            {
                // Profile-based autotune
                result = await stderr.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Profiling embeddings...", async _ =>
                    {
                        return await AutotuneRunner.ProfileBasedAutotuneAsync(
                            store, profileOptions, token);
                    });
            }

            // Print profile summary
            var p = result.Profile;
            CliContext.Console.WriteLine($"  Model: {p.Model} ({p.Dimensions} dims)");
            CliContext.Console.WriteLine($"  Corpus: {p.TotalChunks} chunks, similarity: median={p.Median:F3}, P25={p.P25:F3}");
            CliContext.Console.WriteLine();

            // Print recommended config
            var defaults = new SearchConfig();
            var c = result.Config;
            CliContext.Console.WriteLine("Recommended search config:");
            CliContext.Console.WriteLine($"  VecOnlyGateThreshold: {c.VecOnlyGateThreshold:F2}  (derived from P25)");
            CliContext.Console.WriteLine($"  RerankGateThreshold:  {c.RerankGateThreshold:F2}  (default)");
            CliContext.Console.WriteLine($"  ConfidenceGapRatio:   {c.ConfidenceGapRatio:F2}  ({(c.ConfidenceGapRatio == defaults.ConfidenceGapRatio ? "default" : "tuned")})");
            CliContext.Console.WriteLine($"  FtsMinSignal:         {c.FtsMinSignal:F2}  ({(c.FtsMinSignal == defaults.FtsMinSignal ? "default" : "tuned")})");

            // Print bench results if available
            if (result.BaselineBench != null)
            {
                CliContext.Console.WriteLine();
                var baselineF1 = result.BaselineBench.Summary.TryGetValue("hybrid", out var bs) ? bs.AvgF1 : 0.0;
                CliContext.Console.WriteLine($"Baseline hybrid F1: {baselineF1:F3}");

                if (result.ImprovedOverBaseline && result.TunedBench != null)
                {
                    var tunedF1 = result.TunedBench.Summary.TryGetValue("hybrid", out var ts) ? ts.AvgF1 : 0.0;
                    var delta = tunedF1 - baselineF1;
                    CliContext.Console.WriteLine($"Tuned hybrid F1:    {tunedF1:F3}  ({(delta >= 0 ? "+" : "")}{delta:F3})");
                }
                else if (!result.ImprovedOverBaseline)
                {
                    CliContext.Console.WriteLine("Grid search found no improvement over baseline. Saving profile-only config.");
                }
            }

            // Save unless dry-run
            if (!dryRun)
            {
                await store.SaveSearchConfigAsync(result.Config);
                CliContext.Console.WriteLine();
                CliContext.Console.WriteLine("Config saved. Run 'qmd autotune --reset' to revert to defaults.");
            }
            else
            {
                CliContext.Console.WriteLine();
                CliContext.Console.WriteLine("Dry run — config not saved.");
            }

            return 0;
        });

        return cmd;
    }
}
