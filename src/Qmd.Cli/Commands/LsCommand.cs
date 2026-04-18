using System.CommandLine;
using Qmd.Core.Paths;
using Spectre.Console;

namespace Qmd.Cli.Commands;

public static class LsCommand
{
    public static Command Create()
    {
        var pathArg = new Argument<string?>("path")
        {
            Description = "Collection name or qmd://collection/path prefix to list",
            DefaultValueFactory = _ => null
        };

        var cmd = new Command("ls", "List collections or files in a collection")
        {
            pathArg
        };

        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var pathArgVal = parseResult.GetValue(pathArg);
            await using var store = await CliHelper.CreateStoreAsync();
            var collections = await store.ListCollectionsAsync();

            if (pathArgVal == null)
            {
                // List all collections
                if (collections.Count == 0)
                {
                    CliContext.Console.WriteLine("No collections found. Run 'qmd collection add .' to index files.");
                    return;
                }

                AnsiConsole.MarkupLine("[bold]Collections:[/]");
                CliContext.Console.WriteLine();
                foreach (var coll in collections)
                {
                    var status = await store.GetStatusAsync();
                    var collInfo = status.Collections.FirstOrDefault(c => c.Name == coll.Name);
                    var fileCount = collInfo?.Documents ?? 0;
                    AnsiConsole.MarkupLine($"  [dim]qmd://[/][cyan]{coll.Name}/[/]  [dim]({fileCount} files)[/]");
                }
                return;
            }

            // Parse path argument
            string collectionName;
            string? pathPrefix = null;

            if (pathArgVal.StartsWith("qmd://"))
            {
                var parsed = VirtualPaths.Parse(pathArgVal);
                if (parsed == null)
                {
                    CliContext.Console.WriteErrorLine($"Invalid virtual path: {pathArgVal}");
                    return;
                }
                collectionName = parsed.CollectionName;
                pathPrefix = string.IsNullOrEmpty(parsed.Path) ? null : parsed.Path;
            }
            else
            {
                var parts = pathArgVal.Split('/', 2);
                collectionName = parts[0];
                if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                    pathPrefix = parts[1];
            }

            var coll2 = collections.FirstOrDefault(c => c.Name == collectionName);
            if (coll2 == null)
            {
                CliContext.Console.WriteErrorLine($"Collection not found: {collectionName}");
                CliContext.Console.WriteErrorLine("Run 'qmd ls' to see available collections.");
                return;
            }

            // Query files using SQL LIKE prefix match
            var files = await store.ListFilesAsync(collectionName, pathPrefix, token);

            if (files.Count == 0)
            {
                if (pathPrefix != null)
                    CliContext.Console.WriteLine($"No files found under qmd://{collectionName}/{pathPrefix}");
                else
                    CliContext.Console.WriteLine($"No files found in collection: {collectionName}");
                return;
            }

            foreach (var file in files)
            {
                var sizeStr = FormatBytes(file.BodyLength).PadLeft(8);
                AnsiConsole.MarkupLine($"{sizeStr}  [dim]qmd://{collectionName}/[/][cyan]{file.DisplayPath}[/]");
            }
        });

        return cmd;
    }

    private static string FormatBytes(int bytes)
    {
        if (bytes < 1024) return $"{bytes}B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
        return $"{bytes / (1024.0 * 1024.0):F1}MB";
    }
}
