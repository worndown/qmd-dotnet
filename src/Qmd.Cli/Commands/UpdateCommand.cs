using System.CommandLine;
using System.Diagnostics;
using Qmd.Cli.Progress;
using Qmd.Core;
using Spectre.Console;

namespace Qmd.Cli.Commands;

public static class UpdateCommand
{
    public static Command Create()
    {
        var pullOpt = new Option<bool>("--pull") { Description = "Git pull each collection before re-indexing" };
        var cmd = new Command("update", "Re-index all collections") { pullOpt };
        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var pull = parseResult.GetValue(pullOpt);
            await using var store = await CliHelper.CreateStoreAsync();

            // Execute custom update commands and optional git pull per collection
            var collections = await store.ListCollectionsAsync();
            foreach (var coll in collections)
            {
                if (!Directory.Exists(coll.Path)) continue;

                // Run custom update command if set (e.g., git pull, rsync, etc.)
                if (!string.IsNullOrEmpty(coll.Update))
                {
                    AnsiConsole.MarkupLine($"[dim]{coll.Name}: {coll.Update}[/]");
                    var psi = new ProcessStartInfo
                    {
                        FileName = OperatingSystem.IsWindows() ? "cmd" : "bash",
                        Arguments = OperatingSystem.IsWindows() ? $"/c {coll.Update}" : $"-c {coll.Update}",
                        WorkingDirectory = coll.Path,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                        await proc.WaitForExitAsync();
                }
                else if (pull)
                {
                    AnsiConsole.MarkupLine($"[dim]git pull {coll.Name}...[/]");
                    var psi = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = "pull",
                        WorkingDirectory = coll.Path,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                        await proc.WaitForExitAsync();
                }
            }

            AnsiConsole.MarkupLine("[yellow]Re-indexing collections...[/]");

            var startTime = Stopwatch.GetTimestamp();
            var isTty = OscProgress.IsTty;

            OscProgress.Indeterminate();

            var result = await store.UpdateAsync(new UpdateOptions
            {
                OnProgress = info =>
                {
                    var percent = (int)(info.Current / (double)info.Total * 100);
                    OscProgress.Set(percent);

                    if (isTty)
                    {
                        var elapsed = Stopwatch.GetElapsedTime(startTime).TotalSeconds;
                        var rate = info.Current / elapsed;
                        var remaining = (info.Total - info.Current) / rate;
                        var eta = info.Current > 2 ? $" ETA: {ProgressFormatting.FormatEta(remaining)}" : "";
                        Console.Error.Write($"\rIndexing: {info.Current}/{info.Total}{eta}        ");
                    }
                },
            });

            OscProgress.Clear();
            if (isTty)
                Console.Error.Write("\r                                                \r");

            AnsiConsole.MarkupLine($"  Indexed: [green]{result.Indexed}[/]  Updated: {result.Updated}  Unchanged: {result.Unchanged}  Removed: {result.Removed}");
        });
        return cmd;
    }
}
