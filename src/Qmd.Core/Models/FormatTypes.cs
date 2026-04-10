namespace Qmd.Core.Models;

public enum OutputFormat { Cli, Csv, Md, Xml, Files, Json }

public class FormatOptions
{
    public bool Full { get; set; }
    public string? Query { get; set; }
    public bool UseColor { get; set; }
    public bool LineNumbers { get; set; }
    public string? Intent { get; set; }
    public string? EditorUri { get; set; }
    public bool Explain { get; set; }
}

public class MultiGetFile
{
    public string Filepath { get; set; } = "";
    public string DisplayPath { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Context { get; set; }
    public bool Skipped { get; set; }
    public string? SkipReason { get; set; }
}
