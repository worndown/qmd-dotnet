using Qmd.Core.Chunking;

namespace Qmd.Core.Tests.Chunking;

/// <summary>
/// Approximate tokenizer: 3 chars per token. Test-only.
/// </summary>
internal class CharBasedTokenizer : ITokenizer
{
    public int CountTokens(string text)
    {
        return (int)Math.Ceiling(text.Length / (double)ChunkConstants.AvgCharsPerToken);
    }
}
