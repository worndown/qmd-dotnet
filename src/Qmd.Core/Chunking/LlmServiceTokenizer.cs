using Qmd.Core.Llm;

namespace Qmd.Core.Chunking;

/// <summary>
/// Adapter bridging ILlmService.CountTokens() to ITokenizer,
/// enabling token-accurate chunking via the embed model's tokenizer.
/// </summary>
public class LlmServiceTokenizer(ILlmService llmService) : ITokenizer
{
    public int CountTokens(string text) => llmService.CountTokens(text);
}
