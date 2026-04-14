using System.CommandLine;
using Qmd.Core;
using Qmd.Cli.Formatting;
using Qmd.Core.Models;

namespace Qmd.Cli.Commands;

public static class MultiGetCommand
{
    public static Command Create()
    {
        var patternArg = new Argument<string>("pattern") { Description = "Glob pattern or comma-separated list of paths/docids" };
        var maxLinesOpt = new Option<int?>("--lines", "-l") { Description = "Max lines per file" };
        var maxBytesOpt = new Option<int>("--max-bytes") { Description = "Skip files larger than this (default 10KB)", DefaultValueFactory = _ => 10 * 1024 };
        var formatOpt = new Option<string>("--format") { Description = "Output format: cli, json, csv, md, xml, files", DefaultValueFactory = _ => "cli" };
        var lineNumbersOpt = new Option<bool>("--line-numbers") { Description = "Add line numbers" };
        var (jsonOpt, csvOpt, mdOpt, xmlOpt, filesOpt) = CliHelper.CreateFormatAliasOptions();

        var cmd = new Command("multi-get", "Get multiple documents by glob or comma-separated list")
        {
            patternArg, maxLinesOpt, maxBytesOpt, formatOpt, lineNumbersOpt,
            jsonOpt, csvOpt, mdOpt, xmlOpt, filesOpt
        };

        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var pattern = parseResult.GetValue(patternArg) ?? throw new InvalidOperationException("Required argument 'pattern' was not provided.");
            var maxLines = parseResult.GetValue(maxLinesOpt);
            var maxBytes = parseResult.GetValue(maxBytesOpt);
            var format = parseResult.GetValue(formatOpt) ?? "cli";
            var lineNumbers = parseResult.GetValue(lineNumbersOpt);

            var outputFormat = CliHelper.ResolveFormat(format,
                parseResult.GetValue(jsonOpt),
                parseResult.GetValue(csvOpt),
                parseResult.GetValue(mdOpt),
                parseResult.GetValue(xmlOpt),
                parseResult.GetValue(filesOpt));

            await using var store = await CliHelper.CreateStoreAsync();
            var (docs, errors) = await store.MultiGetAsync(pattern, new MultiGetOptions
            {
                MaxBytes = maxBytes,
                IncludeBody = true,
            });

            foreach (var error in errors)
                CliContext.Console.WriteErrorLine(error);

            if (docs.Count == 0 && errors.Count == 0)
            {
                CliContext.Console.WriteErrorLine($"No files matched: {pattern}");
                return;
            }

            // Convert MultiGetResult → MultiGetFile for formatter
            var files = docs.Select(item =>
            {
                var body = item.Doc.Body ?? "";
                if (!item.Skipped && maxLines.HasValue)
                {
                    var allLines = body.Split('\n');
                    if (allLines.Length > maxLines.Value)
                    {
                        body = string.Join('\n', allLines.Take(maxLines.Value))
                            + $"\n\n[... truncated {allLines.Length - maxLines.Value} more lines]";
                    }
                }
                if (!item.Skipped && lineNumbers)
                    body = FormatHelpers.AddLineNumbers(body);

                return new MultiGetFile
                {
                    Filepath = item.Doc.Filepath,
                    DisplayPath = item.Doc.DisplayPath,
                    Title = item.Doc.Title,
                    Body = body,
                    Context = item.Doc.Context,
                    Skipped = item.Skipped,
                    SkipReason = item.SkipReason,
                };
            }).ToList();

            CliContext.Console.Write(DocumentFormatter.Format(files, outputFormat));
        });

        return cmd;
    }
}
