namespace Qmd.Core.Models;

public record ExpandedQuery(string Type, string Query, int? Line = null);

public record SnippetResult(int Line, string Snippet, int LinesBefore, int LinesAfter, int SnippetLines);
