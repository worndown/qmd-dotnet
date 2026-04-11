namespace Qmd.Core.Chunking;

/// <summary>
/// Approximate tokenizer: 3 chars per token.
/// </summary>
internal class CharBasedTokenizer : ITokenizer
{
    private const int CharsPerToken = 3;

    public int CountTokens(string text)
    {
        return (int)Math.Ceiling(text.Length / (double)CharsPerToken);
    }
}
