using System.Text.RegularExpressions;

namespace Qmd.Core.Llm;

/// <summary>
/// Format text for embedding models. Two strategies:
/// - EmbeddingGemma (default): nomic-style task prefix
/// - Qwen3-Embedding: instruct format
/// </summary>
internal static class EmbeddingFormatter
{
    private static readonly Regex Qwen3Pattern = new(@"qwen.*embed|embed.*qwen", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsQwen3EmbeddingModel(string modelUri)
    {
        return Qwen3Pattern.IsMatch(modelUri);
    }

    public static string FormatQueryForEmbedding(string query, string? modelUri = null)
    {
        var uri = modelUri
            ?? Environment.GetEnvironmentVariable("QMD_EMBED_MODEL")
            ?? LlmConstants.DefaultEmbedModel;

        if (IsQwen3EmbeddingModel(uri))
            return $"Instruct: Retrieve relevant documents for the given query\nQuery: {query}";

        return $"task: search result | query: {query}";
    }

    public static string FormatDocForEmbedding(string text, string? title = null, string? modelUri = null)
    {
        var uri = modelUri
            ?? Environment.GetEnvironmentVariable("QMD_EMBED_MODEL")
            ?? LlmConstants.DefaultEmbedModel;

        if (IsQwen3EmbeddingModel(uri))
            return title != null ? $"{title}\n{text}" : text;

        return $"title: {title ?? "none"} | text: {text}";
    }
}
