namespace Qmd.Cli;

internal class SystemConsoleOutput : IConsoleOutput
{
    public void Write(string text) => Console.Write(text);
    public void WriteLine(string text) => Console.WriteLine(text);
    public void WriteLine() => Console.WriteLine();
    public void WriteError(string text) => Console.Error.Write(text);
    public void WriteErrorLine(string text) => Console.Error.WriteLine(text);
    public bool IsOutputRedirected => Console.IsOutputRedirected;
    public bool IsErrorRedirected => Console.IsErrorRedirected;
    public bool IsInputRedirected => Console.IsInputRedirected;
    public string? ReadLine() => Console.ReadLine();
}
