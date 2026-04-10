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

/// <summary>
/// Approximate tokenizer: 3 chars per token (matches TS avgCharsPerToken for mixed content).
/// </summary>
public class CharBasedTokenizer : ITokenizer
{
    private const int CharsPerToken = 3;

    public int CountTokens(string text)
    {
        return (int)Math.Ceiling(text.Length / (double)CharsPerToken);
    }
}
