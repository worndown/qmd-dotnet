namespace Qmd.Core.Chunking;

/// <summary>
/// Tokenizer interface for token-accurate chunking.
/// Implemented by CharBasedTokenizer (approximation) and
/// LlmServiceTokenizer (real model tokenizer via ILlmService).
/// </summary>
public interface ITokenizer
{
    int CountTokens(string text);
}
