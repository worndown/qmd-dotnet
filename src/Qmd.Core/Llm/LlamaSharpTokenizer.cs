using LLama;
using Qmd.Core.Chunking;

namespace Qmd.Core.Llm;

/// <summary>
/// Real tokenizer using LLamaSharp model weights.
/// </summary>
internal class LlamaSharpTokenizer : ITokenizer
{
    private readonly LLamaWeights weights;

    public LlamaSharpTokenizer(LLamaWeights weights)
    {
        this.weights = weights;
    }

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var tokens = this.weights.NativeHandle.Tokenize(text, false, false, System.Text.Encoding.UTF8);
        return tokens.Length;
    }
}
