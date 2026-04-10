using System.CommandLine;
using System.Text.RegularExpressions;

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
            var file = parseResult.GetValue(fileArg);
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
            var result = await store.GetAsync(file, new Qmd.Sdk.GetOptions { IncludeBody = true });
            if (result.IsFound)
            {
                var doc = result.Document!;
                Console.WriteLine($"# {doc.DisplayPath}");
                if (doc.Context != null) Console.WriteLine($"Context: {doc.Context}");
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
                        Console.WriteLine(Qmd.Core.Formatting.FormatHelpers.AddLineNumbers(body, fromLine ?? 1));
                    else
                        Console.WriteLine(body);
                }
            }
            else
            {
                Console.Error.WriteLine($"Not found: {file}");
                if (result.NotFound!.SimilarFiles.Count > 0)
                {
                    Console.Error.WriteLine("Similar files:");
                    foreach (var f in result.NotFound.SimilarFiles)
                        Console.Error.WriteLine($"  {f}");
                }
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }
}
