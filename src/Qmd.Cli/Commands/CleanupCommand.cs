using System.CommandLine;
using Qmd.Core.Indexing;
using Qmd.Core.Paths;
using Qmd.Core.Store;
using Spectre.Console;

namespace Qmd.Cli.Commands;

public static class CleanupCommand
{
    public static Command Create()
    {
        var cmd = new Command("cleanup", "Clear caches, remove orphans, and vacuum database");

        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            // Use QmdStore directly for maintenance operations
            QmdPaths.EnableProductionMode();
            var dbPath = QmdPaths.GetDefaultDbPath();
            using var store = new QmdStore(dbPath);

            var cacheCount = store.DeleteLLMCache();
            AnsiConsole.MarkupLine($"[green]\u2713[/] Cleared {cacheCount} cached API responses");

            // Remove documents belonging to collections that no longer exist
            var orphanedDocs = store.Db.Prepare(@"
                DELETE FROM documents WHERE collection NOT IN (
                    SELECT name FROM store_collections
                )
            ").Run().Changes;
            if (orphanedDocs > 0)
                AnsiConsole.MarkupLine($"[green]\u2713[/] Removed {orphanedDocs} documents from deleted collections");

            var orphanedContent = store.CleanupOrphanedContent();
            if (orphanedContent > 0)
                AnsiConsole.MarkupLine($"[green]\u2713[/] Removed {orphanedContent} orphaned content entries");
            else
                AnsiConsole.MarkupLine("[dim]No orphaned content to remove[/]");

            var orphanedVectors = MaintenanceOperations.CleanupOrphanedVectors(store.Db);
            if (orphanedVectors > 0)
                AnsiConsole.MarkupLine($"[green]\u2713[/] Removed {orphanedVectors} orphaned vector entries");

            var inactiveDocs = store.DeleteInactiveDocuments();
            if (inactiveDocs > 0)
                AnsiConsole.MarkupLine($"[green]\u2713[/] Removed {inactiveDocs} inactive document records");

            store.VacuumDatabase();
            AnsiConsole.MarkupLine("[green]\u2713[/] Database vacuumed");
        });

        return cmd;
    }
}
