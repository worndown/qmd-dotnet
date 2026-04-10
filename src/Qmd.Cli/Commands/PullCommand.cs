using System.CommandLine;
using Qmd.Core.Llm;
using Qmd.Llm;
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
            var resolver = new ModelResolver();
            var models = new[]
            {
                ("embed", LlmConstants.DefaultEmbedModel),
                ("rerank", LlmConstants.DefaultRerankModel),
                ("generate", LlmConstants.DefaultGenerateModel),
            };

            foreach (var (role, uri) in models)
            {
                AnsiConsole.MarkupLine($"[yellow]{role}:[/] {uri}");
                try
                {
                    var path = await resolver.ResolveModelFileAsync(uri, refresh);
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
