using System.CommandLine;
using System.Diagnostics;
using Qmd.Core.Chunking;
using Qmd.Core.Llm;
using Qmd.Core.Paths;
using Spectre.Console;

namespace Qmd.Cli.Commands;

public static class StatusCommand
{
    public static Command Create()
    {
        var cmd = new Command("status", "Show index health and collection status");
        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            await using var store = await CliHelper.CreateStoreAsync();
            var status = await store.GetStatusAsync();
            var health = await store.GetIndexHealthAsync();

            AnsiConsole.MarkupLine($"[bold]QMD Index Status[/]");
            AnsiConsole.MarkupLine($"  Documents: [green]{status.TotalDocuments}[/]");
            AnsiConsole.MarkupLine($"  Needs embedding: [yellow]{status.NeedsEmbedding}[/]");
            AnsiConsole.MarkupLine($"  Vector index: {(status.HasVectorIndex ? "[green]yes[/]" : "[red]no[/]")}");

            if (health.DaysStale.HasValue)
            {
                var color = health.DaysStale.Value > 7 ? "red" : health.DaysStale.Value > 1 ? "yellow" : "green";
                AnsiConsole.MarkupLine($"  Last update: [{color}]{health.DaysStale.Value} days ago[/]");
            }

            // MCP daemon status
            var pidPath = QmdPaths.GetMcpPidPath();
            if (File.Exists(pidPath))
            {
                var pidText = (await File.ReadAllTextAsync(pidPath, token)).Trim();
                if (int.TryParse(pidText, out var pid))
                {
                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        if (!proc.HasExited)
                            AnsiConsole.MarkupLine($"  MCP daemon: [green]running[/] (PID {pid})");
                        else
                            File.Delete(pidPath);
                    }
                    catch (ArgumentException)
                    {
                        File.Delete(pidPath);
                    }
                }
            }

            // Models
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Models[/]");
            AnsiConsole.MarkupLine($"  Embed:    [dim]{LlmServiceFactory.DefaultEmbedModel}[/]");
            AnsiConsole.MarkupLine($"  Rerank:   [dim]{LlmServiceFactory.DefaultRerankModel}[/]");
            AnsiConsole.MarkupLine($"  Generate: [dim]{LlmServiceFactory.DefaultGenerateModel}[/]");

            // AST chunking
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]AST Chunking[/]");
            var langs = Enum.GetNames<SupportedLanguage>();
            AnsiConsole.MarkupLine($"  Languages: [cyan]{string.Join(", ", langs)}[/]");

            // Collections
            if (status.Collections.Count > 0)
            {
                AnsiConsole.WriteLine();
                var table = new Table();
                table.AddColumn("Collection");
                table.AddColumn("Documents");
                table.AddColumn("Last Updated");
                table.AddColumn("Path");
                foreach (var c in status.Collections)
                    table.AddRow(
                        c.Name,
                        c.Documents.ToString(),
                        string.IsNullOrEmpty(c.LastUpdated) ? "[dim]-[/]" : c.LastUpdated,
                        c.Path ?? "");
                AnsiConsole.Write(table);
            }

            // Warnings
            if (status.NeedsEmbedding > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[yellow]⚠ {status.NeedsEmbedding} documents need embedding. Run:[/] qmd embed");
            }
        });
        return cmd;
    }
}
