namespace Qmd.Core.Models;

public record EmbeddingResult(float[] Embedding, string Model);

public record TokenLogProb(string Token, double LogProb);

public record GenerateResult(string Text, string Model, List<TokenLogProb>? LogProbs, bool Done);

public record RerankDocumentResult(string File, double Score, int Index);

public record RerankResult(List<RerankDocumentResult> Results, string Model);

public record ModelInfo(string Name, bool Exists, string? Path = null);

public class EmbedOptions
{
    public string? Model { get; init; }
    public bool IsQuery { get; init; }
    public string? Title { get; init; }
}

public class GenerateOptions
{
    public string? Model { get; init; }
    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
}

public class RerankOptions
{
    public string? Model { get; init; }
}

public class ExpandQueryOptions
{
    public string? Context { get; init; }
    public bool IncludeLexical { get; init; } = true;
}

public enum QueryType { Lex, Vec, Hyde }

public record QueryExpansion(QueryType Type, string Text);

public record RerankDocument(string File, string Text, string? Title = null);
