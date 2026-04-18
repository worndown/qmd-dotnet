namespace Qmd.Core.Chunking;

/// <summary>
/// Tokenizer interface for token-accurate chunking.
/// </summary>
public interface ITokenizer
{
    int CountTokens(string text);
}
