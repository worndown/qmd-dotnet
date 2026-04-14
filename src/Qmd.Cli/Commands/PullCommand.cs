using System.CommandLine;
using Qmd.Core.Llm;
using Spectre.Console;

namespace Qmd.Cli.Commands;

public static class PullCommand
{
    public static Command Create()
    {
        var refreshOpt = new Option<bool>("--refresh") { Description = "Re-download even if cached" };
        var cmd = new Command("pull", "Download LLM models (embed, rerank, generate)") { refreshOpt };

        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var refresh = parseResult.GetValue(refreshOpt);
            var models = new[]
            {
                ("embed",    LlmServiceFactory.DefaultEmbedModel),
                ("rerank",   LlmServiceFactory.DefaultRerankModel),
                ("generate", LlmServiceFactory.DefaultGenerateModel),
            };

            foreach (var (role, uri) in models)
            {
                AnsiConsole.MarkupLine($"[yellow]{role}:[/] {uri}");
                try
                {
                    var path = await LlmServiceFactory.ResolveModelAsync(uri, refresh,
                        new Progress<string>(msg => AnsiConsole.MarkupLine($"  [grey]{msg}[/]")), token);
                    AnsiConsole.MarkupLine($"  [green]OK[/] {path}");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"  [red]Error:[/] {ex.Message}");
                }
            }
        });

        return cmd;
    }
}
