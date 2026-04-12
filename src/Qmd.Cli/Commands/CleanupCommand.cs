using System.CommandLine;
using Qmd.Core;
using Qmd.Core.Paths;
using Spectre.Console;

namespace Qmd.Cli.Commands;

public static class CleanupCommand
{
    public static Command Create()
    {
        var cmd = new Command("cleanup", "Clear caches, remove orphans, and vacuum database");

        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            QmdPaths.EnableProductionMode();
            var dbPath = QmdPaths.GetDefaultDbPath();
            await using var store = await QmdStoreFactory.CreateAsync(new StoreOptions { DbPath = dbPath });

            var result = await store.CleanupAsync(ct: token);

            AnsiConsole.MarkupLine($"[green]\u2713[/] Cleared {result.CacheEntriesDeleted} cached API responses");

            if (result.OrphanedCollectionDocsDeleted > 0)
                AnsiConsole.MarkupLine($"[green]\u2713[/] Removed {result.OrphanedCollectionDocsDeleted} documents from deleted collections");

            if (result.OrphanedContentDeleted > 0)
                AnsiConsole.MarkupLine($"[green]\u2713[/] Removed {result.OrphanedContentDeleted} orphaned content entries");
            else
                AnsiConsole.MarkupLine("[dim]No orphaned content to remove[/]");

            if (result.OrphanedVectorsDeleted > 0)
                AnsiConsole.MarkupLine($"[green]\u2713[/] Removed {result.OrphanedVectorsDeleted} orphaned vector entries");

            if (result.InactiveDocsDeleted > 0)
                AnsiConsole.MarkupLine($"[green]\u2713[/] Removed {result.InactiveDocsDeleted} inactive document records");

            AnsiConsole.MarkupLine("[green]\u2713[/] Database vacuumed");
        });

        return cmd;
    }
}
