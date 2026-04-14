using System.Text;

namespace Qmd.Cli.Tests;

internal class TestConsoleOutput : IConsoleOutput
{
    private readonly StringBuilder _output = new();
    private readonly StringBuilder _error = new();

    public void Write(string text) => _output.Append(text);
    public void WriteLine(string text) => _output.AppendLine(text);
    public void WriteLine() => _output.AppendLine();
    public void WriteError(string text) => _error.Append(text);
    public void WriteErrorLine(string text) => _error.AppendLine(text);
    public bool IsOutputRedirected => true;
    public bool IsErrorRedirected => true;
    public bool IsInputRedirected => true;
    public string? ReadLine() => null;

    public string GetOutput() => _output.ToString();
    public string GetError() => _error.ToString();
    public void Clear() { _output.Clear(); _error.Clear(); }
}
