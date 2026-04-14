namespace Qmd.Cli;

/// <summary>
/// Abstraction over console I/O for testability.
/// </summary>
internal interface IConsoleOutput
{
    void Write(string text);
    void WriteLine(string text);
    void WriteLine();
    void WriteError(string text);
    void WriteErrorLine(string text);
    bool IsOutputRedirected { get; }
    bool IsErrorRedirected { get; }
    bool IsInputRedirected { get; }
    string? ReadLine();
}
