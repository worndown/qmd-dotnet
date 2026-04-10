using System.CommandLine;
using System.Reflection;
using LLama.Native;
using Qmd.Cli.Commands;

// Version from assembly
var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";

var root = new RootCommand("QMD - Query Markup Documents (.NET)");

// Global options
var indexOpt = new Option<string?>("--index") { Description = "Named index to use (default: index)" };
indexOpt.Recursive = true;
root.Options.Add(indexOpt);

root.Subcommands.Add(SearchCommand.Create());
root.Subcommands.Add(QueryCommand.Create());
root.Subcommands.Add(VsearchCommand.Create());
root.Subcommands.Add(GetCommand.Create());
root.Subcommands.Add(MultiGetCommand.Create());
root.Subcommands.Add(LsCommand.Create());
root.Subcommands.Add(CollectionCommand.Create());
root.Subcommands.Add(ContextCommand.Create());
root.Subcommands.Add(StatusCommand.Create());
root.Subcommands.Add(UpdateCommand.Create());
root.Subcommands.Add(EmbedCommand.Create());
root.Subcommands.Add(CleanupCommand.Create());
root.Subcommands.Add(McpCommand.Create());
root.Subcommands.Add(PullCommand.Create());
root.Subcommands.Add(SkillCommand.Create());
root.Subcommands.Add(BenchCommand.Create());

NativeLibraryConfig.All.WithLogCallback((level, message) =>
{
    if (level >= LLamaLogLevel.Error)
        Console.Error.Write(message);
});

// Parse first, capture global option, then invoke
var parseResult = root.Parse(args);
var indexVal = parseResult.GetValue(indexOpt);
if (indexVal != null)
    CliHelper.IndexName = indexVal;
return await parseResult.InvokeAsync();
