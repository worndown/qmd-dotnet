using System.CommandLine;
using Qmd.Core;
using Qmd.Core.Paths;
using Spectre.Console;

namespace Qmd.Cli.Commands;

public static class ContextCommand
{
    public static Command Create()
    {
        var cmd = new Command("context", "Manage collection contexts");

        // context list
        var listCmd = new Command("list", "List all contexts");
        listCmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            await using var store = await CliHelper.CreateStoreAsync();
            var contexts = await store.ListContextsAsync();
            var table = new Table();
            table.AddColumn("Collection");
            table.AddColumn("Path");
            table.AddColumn("Context");
            foreach (var (coll, path, ctx) in contexts)
                table.AddRow(coll, path, ctx);
            AnsiConsole.Write(table);
        });

        // context add [path] "text"
        var addPath = new Argument<string>("path") { Description = "Path prefix or virtual path (qmd://collection/path)", DefaultValueFactory = _ => "." };
        var addText = new Argument<string>("text") { Description = "Context description" };
        var addCmd = new Command("add", "Add context to a path") { addPath, addText };
        addCmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var path = parseResult.GetValue(addPath) ?? ".";
            var text = parseResult.GetValue(addText) ?? throw new InvalidOperationException("Required argument 'text' was not provided.");
            await using var store = await CliHelper.CreateStoreAsync();
            await HandleAddAsync(store, path, text);
        });

        // context rm <path>
        var rmPath = new Argument<string>("path") { Description = "Path to remove context from (or virtual path)" };
        var rmCmd = new Command("rm", "Remove context") { rmPath };
        rmCmd.Aliases.Add("remove");
        rmCmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var path = parseResult.GetValue(rmPath) ?? throw new InvalidOperationException("Required argument 'path' was not provided.");
            await using var store = await CliHelper.CreateStoreAsync();
            await HandleRemoveAsync(store, path);
        });

        // context check
        var checkCmd = new Command("check", "Check for collections or paths missing context");
        checkCmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            await using var store = await CliHelper.CreateStoreAsync();
            var collections = await store.ListCollectionsAsync();
            var contexts = await store.ListContextsAsync();

            var collectionsWithContext = contexts.Select(c => c.Collection).ToHashSet();
            var missing = collections.Where(c => !collectionsWithContext.Contains(c.Name)).ToList();

            if (missing.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]All collections have context.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Collections missing context:[/]");
                foreach (var c in missing)
                    AnsiConsole.MarkupLine($"  [red]{c.Name}[/] ({c.Path})");
                AnsiConsole.MarkupLine($"\nUse [cyan]qmd context add qmd://<collection>/ \"description\"[/] to add context.");
            }
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(addCmd);
        cmd.Subcommands.Add(rmCmd);
        cmd.Subcommands.Add(checkCmd);
        return cmd;
    }

    internal static async Task HandleAddAsync(IQmdStore store, string path, string text)
    {
        var collections = await store.ListCollectionsAsync();
        if (collections.Count == 0) { CliContext.Console.WriteErrorLine("No collections found."); return; }

        string collectionName;
        string pathPrefix;

        if (VirtualPaths.IsVirtualPath(path))
        {
            var parsed = VirtualPaths.Parse(path)!;
            collectionName = parsed.CollectionName;
            pathPrefix = parsed.Path ?? "/";
        }
        else if (path == "/")
        {
            await store.SetGlobalContextAsync(text);
            CliContext.Console.WriteLine($"Global context set: {text}");
            return;
        }
        else
        {
            var absPath = Path.GetFullPath(path);
            var matched = collections.FirstOrDefault(c =>
                absPath.StartsWith(c.Path, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                collectionName = matched.Name;
                pathPrefix = absPath.Length > matched.Path.Length
                    ? absPath[matched.Path.Length..].Replace('\\', '/')
                    : "/";
            }
            else
            {
                collectionName = collections[0].Name;
                pathPrefix = path;
            }
        }

        var result = await store.AddContextAsync(collectionName, pathPrefix, text);
        CliContext.Console.WriteLine(result
            ? $"Context added to {collectionName}:{pathPrefix}"
            : "Failed to add context.");
    }

    internal static async Task HandleRemoveAsync(IQmdStore store, string path)
    {
        var collections = await store.ListCollectionsAsync();
        if (collections.Count == 0) { CliContext.Console.WriteErrorLine("No collections found."); return; }

        string collectionName;
        string pathPrefix;

        if (VirtualPaths.IsVirtualPath(path))
        {
            var parsed = VirtualPaths.Parse(path)!;
            collectionName = parsed.CollectionName;
            pathPrefix = parsed.Path ?? "/";
        }
        else if (path == "/")
        {
            await store.SetGlobalContextAsync(null);
            CliContext.Console.WriteLine("Global context removed.");
            return;
        }
        else
        {
            var absPath = Path.GetFullPath(path);
            var matched = collections.FirstOrDefault(c =>
                absPath.StartsWith(c.Path, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                collectionName = matched.Name;
                pathPrefix = absPath.Length > matched.Path.Length
                    ? absPath[matched.Path.Length..].Replace('\\', '/')
                    : "/";
            }
            else
            {
                collectionName = collections[0].Name;
                pathPrefix = path;
            }
        }

        var result = await store.RemoveContextAsync(collectionName, pathPrefix);
        CliContext.Console.WriteLine(result ? "Context removed." : "Context not found.");
    }
}
