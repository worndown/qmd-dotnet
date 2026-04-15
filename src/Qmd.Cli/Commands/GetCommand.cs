using System.CommandLine;
using System.Text.RegularExpressions;
using Qmd.Cli.Formatting;
using Qmd.Core;

namespace Qmd.Cli.Commands;

public static class GetCommand
{
    private static readonly Regex ColonLineRegex = new(@":(\d+)$", RegexOptions.Compiled);

    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "File path or docid (#abc123)" };
        var fromLineOpt = new Option<int?>("--from") { Description = "Start line (1-indexed)" };
        var maxLinesOpt = new Option<int?>("--lines", "-l") { Description = "Max lines to return" };
        var lineNumbersOpt = new Option<bool>("--line-numbers") { Description = "Add line numbers" };

        var cmd = new Command("get", "Retrieve a document by path or docid")
        {
            fileArg, fromLineOpt, maxLinesOpt, lineNumbersOpt
        };

        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var file = parseResult.GetValue(fileArg) ?? throw new InvalidOperationException("Required argument 'file' was not provided.");
            var fromLine = parseResult.GetValue(fromLineOpt);
            var maxLines = parseResult.GetValue(maxLinesOpt);
            var lineNumbers = parseResult.GetValue(lineNumbersOpt);

            // Parse :line suffix (e.g., file.md:100) — only if --from not explicitly set
            var colonMatch = ColonLineRegex.Match(file);
            if (colonMatch.Success)
            {
                if (!fromLine.HasValue && int.TryParse(colonMatch.Groups[1].Value, out var parsedLine))
                    fromLine = parsedLine;
                file = file[..^colonMatch.Length];
            }

            await using var store = await CliHelper.CreateStoreAsync();
            await HandleGetAsync(store, file, fromLine, maxLines, lineNumbers, token);
        });

        return cmd;
    }

    internal static async Task HandleGetAsync(IQmdStore store, string file, int? fromLine, int? maxLines, bool lineNumbers,
        CancellationToken ct = default)
    {
        var result = await store.GetAsync(file, new GetOptions { IncludeBody = true }, ct);
        if (result.IsFound)
        {
            var doc = result.Document!;
            CliContext.Console.WriteLine($"# {doc.DisplayPath}");
            if (doc.Context != null) CliContext.Console.WriteLine($"Context: {doc.Context}");
            if (doc.Body != null)
            {
                var body = doc.Body;

                // Apply line slicing
                if (fromLine.HasValue || maxLines.HasValue)
                {
                    var lines = body.Split('\n');
                    var start = (fromLine ?? 1) - 1;
                    var count = maxLines ?? lines.Length - start;
                    body = string.Join('\n', lines.Skip(start).Take(count));
                }

                if (lineNumbers)
                    CliContext.Console.WriteLine(FormatHelpers.AddLineNumbers(body, fromLine ?? 1));
                else
                    CliContext.Console.WriteLine(body);
            }
        }
        else
        {
            CliContext.Console.WriteErrorLine($"Not found: {file}");
            if (result.NotFound!.SimilarFiles.Count > 0)
            {
                CliContext.Console.WriteErrorLine("Similar files:");
                foreach (var f in result.NotFound.SimilarFiles)
                    CliContext.Console.WriteErrorLine($"  {f}");
            }
            Environment.ExitCode = 1;
        }
    }
}
