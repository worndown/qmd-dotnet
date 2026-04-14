using System.CommandLine;
using System.Diagnostics;
using Qmd.Cli.Progress;
using Qmd.Core.Models;
using Spectre.Console;

namespace Qmd.Cli.Commands;

public static class EmbedCommand
{
    public static Command Create()
    {
        var forceOpt = new Option<bool>("--force", "-f") { Description = "Force re-embedding of all documents" };
        var chunkStrategyOpt = new Option<string>("--chunk-strategy") { Description = "Chunking strategy: regex (default) or auto (AST for code files)", DefaultValueFactory = _ => "regex" };
        var maxDocsOpt = new Option<int?>("--max-docs-per-batch") { Description = "Max documents per batch (default: 64)" };
        var maxMbOpt = new Option<int?>("--max-batch-mb") { Description = "Max MB per batch (default: 64)" };

        var cmd = new Command("embed", "Generate vector embeddings") { forceOpt, chunkStrategyOpt, maxDocsOpt, maxMbOpt };
        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var force = parseResult.GetValue(forceOpt);
            var chunkStrategy = parseResult.GetValue(chunkStrategyOpt) ?? "regex";
            var maxDocs = parseResult.GetValue(maxDocsOpt);
            var maxMb = parseResult.GetValue(maxMbOpt);

            var strategy = chunkStrategy.ToLowerInvariant() == "auto"
                ? ChunkStrategy.Auto
                : ChunkStrategy.Regex;

            await using var store = await CliHelper.CreateStoreAsync();
            AnsiConsole.MarkupLine("[yellow]Generating embeddings...[/]");

            var startTime = Stopwatch.GetTimestamp();
            var isTty = OscProgress.IsTty;

            CursorHelper.Hide();
            OscProgress.Indeterminate();

            var result = await store.EmbedAsync(new EmbedPipelineOptions
            {
                Force = force,
                ChunkStrategy = strategy,
                MaxDocsPerBatch = maxDocs ?? 64,
                MaxBatchBytes = (maxMb ?? 64) * 1024 * 1024,
                Progress = new Progress<EmbedProgress>(info =>
                {
                    if (info.TotalBytes == 0) return;

                    var percent = info.BytesProcessed / (double)info.TotalBytes * 100;
                    OscProgress.Set((int)percent);

                    if (isTty)
                    {
                        var elapsed = Stopwatch.GetElapsedTime(startTime).TotalSeconds;
                        var bytesPerSec = info.BytesProcessed / elapsed;
                        var remainingBytes = info.TotalBytes - info.BytesProcessed;
                        var etaSec = remainingBytes / bytesPerSec;

                        var bar = ProgressFormatting.RenderProgressBar(percent);
                        var percentStr = $"{percent:F0}".PadLeft(3);
                        var throughput = $"{ProgressFormatting.FormatBytes((long)bytesPerSec)}/s";
                        var eta = elapsed > 2 ? ProgressFormatting.FormatEta(etaSec) : "...";
                        var errStr = info.Errors > 0 ? $" {info.Errors} err" : "";

                        Console.Error.Write($"\r{bar} {percentStr}% {info.ChunksEmbedded}/{info.TotalChunks}{errStr} {throughput} ETA {eta}   ");
                    }
                }),
            });

            OscProgress.Clear();
            CursorHelper.Show();

            if (result.ChunksEmbedded == 0 && result.DocsProcessed == 0)
            {
                AnsiConsole.MarkupLine("[green]No documents to embed.[/]");
            }
            else
            {
                var totalBar = ProgressFormatting.RenderProgressBar(100);
                var totalTimeSec = result.DurationMs / 1000.0;
                Console.Error.WriteLine($"\r{totalBar} 100%                                    ");
                AnsiConsole.MarkupLine($"\n[green]Done![/] Embedded [bold]{result.ChunksEmbedded}[/] chunks from [bold]{result.DocsProcessed}[/] documents in [bold]{ProgressFormatting.FormatEta(totalTimeSec)}[/]");
                if (result.Errors > 0)
                    AnsiConsole.MarkupLine($"[yellow]{result.Errors} chunks failed[/]");
            }
        });
        return cmd;
    }
}
