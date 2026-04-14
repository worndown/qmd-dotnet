using System.CommandLine;
using Qmd.Core.Models;
using Spectre.Console;

namespace Qmd.Cli.Commands;

public static class ProfileEmbeddingsCommand
{
    public static Command Create()
    {
        var sampleSizeOpt = new Option<int>("--sample-size", "-n") { Description = "Number of random chunks to sample", DefaultValueFactory = _ => 100 };
        var collectionOpt = new Option<string[]>("--collection", "-c") { Description = "Filter by collection(s)", AllowMultipleArgumentsPerToken = true };

        var cmd = new Command("profile-embeddings", "Profile embedding model similarity distribution on indexed corpus")
        {
            sampleSizeOpt, collectionOpt
        };

        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var sampleSize = parseResult.GetValue(sampleSizeOpt);
            var collections = parseResult.GetValue(collectionOpt) ?? [];

            await using var store = await CliHelper.CreateStoreAsync();
            var collList = collections.Length > 0
                ? await CliHelper.ResolveCollectionsAsync(store, collections)
                : null;

            var stderr = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(Console.Error),
            });

            EmbeddingProfile profile = null!;
            await stderr.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Profiling {sampleSize} random chunks...", async _ =>
                {
                    profile = await store.ProfileEmbeddingsAsync(new EmbeddingProfileOptions
                    {
                        SampleSize = sampleSize,
                        Collections = collList,
                    }, token);
                });

            CliContext.Console.WriteLine($"Model: {profile.Model} ({profile.Dimensions} dimensions)");
            CliContext.Console.WriteLine($"Corpus: {profile.TotalChunks} chunks, sampled {profile.SampleSize} ({profile.ScoreCount} score pairs)");
            CliContext.Console.WriteLine();
            CliContext.Console.WriteLine("Cosine similarity distribution (inter-document):");
            CliContext.Console.WriteLine($"  Min:    {profile.Min:F3}");
            CliContext.Console.WriteLine($"  P5:     {profile.P5:F3}    P25:    {profile.P25:F3}");
            CliContext.Console.WriteLine($"  Median: {profile.Median:F3}    Mean:   {profile.Mean:F3}");
            CliContext.Console.WriteLine($"  P75:    {profile.P75:F3}    P95:    {profile.P95:F3}");
            CliContext.Console.WriteLine($"  Max:    {profile.Max:F3}");
            CliContext.Console.WriteLine();
            CliContext.Console.WriteLine($"Suggested --min-score for vsearch: {profile.SuggestedVsearchMinScore:F2} (P75)");
        });

        return cmd;
    }
}
