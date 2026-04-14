namespace Qmd.Cli;

internal static class CliContext
{
    public static IConsoleOutput Console { get; set; } = new SystemConsoleOutput();
}
