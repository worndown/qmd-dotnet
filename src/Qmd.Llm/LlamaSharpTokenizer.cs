using LLama;
using Qmd.Core.Chunking;

namespace Qmd.Llm;

/// <summary>
/// Real tokenizer using LLamaSharp model weights.
/// Replaces CharBasedTokenizer for token-accurate chunking.
/// </summary>
public class LlamaSharpTokenizer : ITokenizer
{
    private readonly LLamaWeights _weights;

    public LlamaSharpTokenizer(LLamaWeights weights)
    {
        _weights = weights;
    }

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var tokens = _weights.NativeHandle.Tokenize(text, false, false, System.Text.Encoding.UTF8);
        return tokens.Length;
    }
}
