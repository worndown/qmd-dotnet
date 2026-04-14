namespace Qmd.Core.Models;

public record ExpandedQuery(string Type, string Query, int? Line = null);

public record SnippetResult(int Line, string Snippet, int LinesBefore, int LinesAfter, int SnippetLines);

public class VectorSearchQueryOptions
{
    public int Limit { get; init; } = 10;
    public double MinScore { get; init; } = 0.3;
    public List<string>? Collections { get; init; }
    public string? Intent { get; init; }
}
