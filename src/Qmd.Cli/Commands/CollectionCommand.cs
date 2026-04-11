using System.CommandLine;
using Qmd.Core;
using Spectre.Console;

namespace Qmd.Cli.Commands;

public static class CollectionCommand
{
    public static Command Create()
    {
        var cmd = new Command("collection", "Manage collections");

        // collection list
        var listCmd = new Command("list", "List all collections");
        listCmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            await using var store = await CliHelper.CreateStoreAsync();
            var status = await store.GetStatusAsync();
            var collections = await store.ListCollectionsAsync();
            var collMap = collections.ToDictionary(c => c.Name);

            var table = new Table();
            table.AddColumn("Name");
            table.AddColumn("Pattern");
            table.AddColumn("Files");
            table.AddColumn("Updated");
            table.AddColumn("Path");

            foreach (var c in status.Collections)
            {
                var coll = collMap.GetValueOrDefault(c.Name);
                var pattern = coll?.Pattern ?? "**/*.md";
                var excluded = coll?.IncludeByDefault == false ? " [dim][excluded][/]" : "";
                var updated = !string.IsNullOrEmpty(c.LastUpdated) ? FormatTimeAgo(c.LastUpdated) : "";
                table.AddRow(
                    $"{Markup.Escape(c.Name)}{excluded}",
                    Markup.Escape(pattern),
                    c.Documents.ToString(),
                    updated,
                    Markup.Escape(c.Path ?? ""));
            }
            AnsiConsole.Write(table);
        });

        // collection add <path> --name <name> --mask <pattern> --ignore <patterns>
        var addPath = new Argument<string>("path") { Description = "Directory path to index", DefaultValueFactory = _ => "." };
        var nameOpt = new Option<string?>("--name") { Description = "Collection name (auto-derived from directory if omitted)" };
        var maskOpt = new Option<string>("--mask") { Description = "Glob pattern", DefaultValueFactory = _ => "**/*.md" };
        var ignoreOpt = new Option<string[]>("--ignore") { Description = "Ignore patterns", AllowMultipleArgumentsPerToken = true };
        var addCmd = new Command("add", "Add a new collection") { addPath, nameOpt, maskOpt, ignoreOpt };
        addCmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var path = parseResult.GetValue(addPath) ?? ".";
            var name = parseResult.GetValue(nameOpt);
            var mask = parseResult.GetValue(maskOpt) ?? "**/*.md";
            var ignore = parseResult.GetValue(ignoreOpt) ?? [];
            // Auto-derive name from directory basename if not provided
            name ??= Path.GetFileName(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(name))
                name = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "collection");
            await using var store = await CliHelper.CreateStoreAsync();
            await store.AddCollectionAsync(name, path, mask,
                ignore.Length > 0 ? ignore.ToList() : null);
            // Auto-index the new collection
            Console.WriteLine($"Indexing '{name}'...");
            var result = await store.UpdateAsync(new UpdateOptions { Collections = [name] });
            Console.WriteLine($"Collection '{name}' added. Indexed {result.Indexed} files.");
        });

        // collection remove <name>
        var removeName = new Argument<string>("name") { Description = "Collection name" };
        var removeCmd = new Command("remove", "Remove a collection") { removeName };
        removeCmd.Aliases.Add("rm");
        removeCmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var name = parseResult.GetValue(removeName) ?? throw new InvalidOperationException("Required argument 'name' was not provided.");
            await using var store = await CliHelper.CreateStoreAsync();
            if (await store.RemoveCollectionAsync(name))
                Console.WriteLine($"Collection '{name}' removed.");
            else
                Console.Error.WriteLine($"Collection '{name}' not found.");
        });

        // collection rename <old> <new>
        var oldName = new Argument<string>("old") { Description = "Current name" };
        var newName = new Argument<string>("new") { Description = "New name" };
        var renameCmd = new Command("rename", "Rename a collection") { oldName, newName };
        renameCmd.Aliases.Add("mv");
        renameCmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var old = parseResult.GetValue(oldName) ?? throw new InvalidOperationException("Required argument 'old' was not provided.");
            var @new = parseResult.GetValue(newName) ?? throw new InvalidOperationException("Required argument 'new' was not provided.");
            await using var store = await CliHelper.CreateStoreAsync();
            if (await store.RenameCollectionAsync(old, @new))
                Console.WriteLine($"Collection '{old}' renamed to '{@new}'.");
            else
                Console.Error.WriteLine($"Collection '{old}' not found.");
        });

        // collection show <name>
        var showNameArg = new Argument<string>("name") { Description = "Collection name" };
        var showCmd = new Command("show", "Show collection details") { showNameArg };
        showCmd.Aliases.Add("info");
        showCmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var name = parseResult.GetValue(showNameArg);
            await using var store = await CliHelper.CreateStoreAsync();
            var collections = await store.ListCollectionsAsync();
            var coll = collections.FirstOrDefault(c => c.Name == name);
            if (coll == null)
            {
                Console.Error.WriteLine($"Collection '{name}' not found.");
                return;
            }
            Console.WriteLine($"Name:    {coll.Name}");
            Console.WriteLine($"Path:    {coll.Path}");
            Console.WriteLine($"Pattern: {coll.Pattern}");
            Console.WriteLine($"Include: {(coll.IncludeByDefault != false ? "yes" : "no")}");
            if (coll.Update != null)
                Console.WriteLine($"Update:  {coll.Update}");
            if (coll.Ignore is { Count: > 0 })
                Console.WriteLine($"Ignore:  {string.Join(", ", coll.Ignore)}");
            if (coll.Context is { Count: > 0 })
            {
                Console.WriteLine("Contexts:");
                foreach (var (path, ctx) in coll.Context)
                    Console.WriteLine($"  {path}: {ctx}");
            }
        });

        // collection update-cmd <name> [command]
        var updateCmdName = new Argument<string>("name") { Description = "Collection name" };
        var updateCmdValue = new Argument<string?>("command") { Description = "Bash command to run before update (omit to clear)", DefaultValueFactory = _ => null };
        var updateCmdCmd = new Command("update-cmd", "Set custom update command for collection") { updateCmdName, updateCmdValue };
        updateCmdCmd.Aliases.Add("set-update");
        updateCmdCmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var name = parseResult.GetValue(updateCmdName) ?? throw new InvalidOperationException("Required argument 'name' was not provided.");
            var command = parseResult.GetValue(updateCmdValue);
            await using var store = await CliHelper.CreateStoreAsync();
            var result = await store.UpdateCollectionSettingsAsync(name,
                update: command, clearUpdate: command == null);
            if (!result) { Console.Error.WriteLine($"Collection '{name}' not found."); return; }
            Console.WriteLine(command != null
                ? $"Update command set for '{name}': {command}"
                : $"Update command cleared for '{name}'.");
        });

        // collection include <name>
        var includeNameArg = new Argument<string>("name") { Description = "Collection name" };
        var includeCmd = new Command("include", "Include collection in default searches") { includeNameArg };
        includeCmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var name = parseResult.GetValue(includeNameArg) ?? throw new InvalidOperationException("Required argument 'name' was not provided.");
            await using var store = await CliHelper.CreateStoreAsync();
            var result = await store.UpdateCollectionSettingsAsync(name, includeByDefault: true);
            if (!result) { Console.Error.WriteLine($"Collection '{name}' not found."); return; }
            Console.WriteLine($"Collection '{name}' included in default searches.");
        });

        // collection exclude <name>
        var excludeNameArg = new Argument<string>("name") { Description = "Collection name" };
        var excludeCmd = new Command("exclude", "Exclude collection from default searches") { excludeNameArg };
        excludeCmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var name = parseResult.GetValue(excludeNameArg) ?? throw new InvalidOperationException("Required argument 'name' was not provided.");
            await using var store = await CliHelper.CreateStoreAsync();
            var result = await store.UpdateCollectionSettingsAsync(name, includeByDefault: false);
            if (!result) { Console.Error.WriteLine($"Collection '{name}' not found."); return; }
            Console.WriteLine($"Collection '{name}' excluded from default searches.");
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(addCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(renameCmd);
        cmd.Subcommands.Add(showCmd);
        cmd.Subcommands.Add(updateCmdCmd);
        cmd.Subcommands.Add(includeCmd);
        cmd.Subcommands.Add(excludeCmd);
        return cmd;
    }

    private static string FormatTimeAgo(string isoDate)
    {
        if (!DateTime.TryParse(isoDate, out var dt)) return "";
        var span = DateTime.UtcNow - dt.ToUniversalTime();
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        return dt.ToString("yyyy-MM-dd");
    }
}
