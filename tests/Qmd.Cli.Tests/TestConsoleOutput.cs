using System.Text;

namespace Qmd.Cli.Tests;

internal class TestConsoleOutput : IConsoleOutput
{
    private readonly StringBuilder output = new();
    private readonly StringBuilder error = new();

    public void Write(string text) => this.output.Append(text);
    public void WriteLine(string text) => this.output.AppendLine(text);
    public void WriteLine() => this.output.AppendLine();
    public void WriteError(string text) => this.error.Append(text);
    public void WriteErrorLine(string text) => this.error.AppendLine(text);
    public bool IsOutputRedirected => true;
    public bool IsErrorRedirected => true;
    public bool IsInputRedirected => true;
    public string? ReadLine() => null;

    public string GetOutput() => this.output.ToString();
    public string GetError() => this.error.ToString();
    public void Clear() {
        this.output.Clear();
        this.error.Clear(); }
}
