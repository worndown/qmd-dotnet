using System.CommandLine;
using System.Text.Json;
using Qmd.Core.Bench;
using Qmd.Sdk.Bench;

namespace Qmd.Cli.Commands;

public static class BenchCommand
{
    public static Command Create()
    {
        var fixtureArg = new Argument<string>("fixture") { Description = "Path to benchmark fixture JSON file" };
        var jsonOpt = new Option<bool>("--json") { Description = "Output results as JSON" };
        var collectionOpt = new Option<string?>("--collection", "-c") { Description = "Override collection filter" };

        var cmd = new Command("bench", "Run benchmark against search backends")
        {
            fixtureArg, jsonOpt, collectionOpt
        };

        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var fixturePath = parseResult.GetValue(fixtureArg);
            var json = parseResult.GetValue(jsonOpt);
            var collection = parseResult.GetValue(collectionOpt);

            // Load and validate fixture
            var resolvedPath = Path.GetFullPath(fixturePath);
            if (!File.Exists(resolvedPath))
            {
                Console.Error.WriteLine($"Fixture file not found: {resolvedPath}");
                return 1;
            }

            var raw = await File.ReadAllTextAsync(resolvedPath);
            var fixture = JsonSerializer.Deserialize<BenchmarkFixture>(raw);
            if (fixture?.Queries is not { Count: > 0 })
            {
                Console.Error.WriteLine("Invalid fixture: missing 'queries' array");
                return 1;
            }

            await using var store = await CliHelper.CreateStoreAsync();

            var result = await BenchmarkRunner.RunBenchmarkAsync(store, fixture, new BenchmarkRunOptions
            {
                Json = json,
                Collection = collection,
            });

            if (json)
            {
                Console.Write(BenchmarkRunner.FormatJson(result));
            }
            else
            {
                Console.Write(BenchmarkRunner.FormatTable(result));
            }

            return 0;
        });

        return cmd;
    }
}
