namespace Qmd.Core.Llm;

internal static class LlmConstants
{
    public const string DefaultEmbedModel = "hf:ggml-org/embeddinggemma-300M-GGUF/embeddinggemma-300M-Q8_0.gguf";
    public const string DefaultRerankModel = "hf:ggml-org/Qwen3-Reranker-0.6B-Q8_0-GGUF/qwen3-reranker-0.6b-q8_0.gguf";
    public const string DefaultGenerateModel = "hf:tobil/qmd-query-expansion-1.7B-gguf/qmd-query-expansion-1.7B-q8_0.gguf";

    public const int EmbedContextSize = 2048;
    public const int RerankContextSize = 4096;
    public const int RerankTemplateOverhead = 512;
    public const int DefaultInactivityTimeoutMs = 5 * 60 * 1000; // 5 min
    public const int DefaultMaxDocsPerBatch = 64;
    public const int DefaultMaxBatchBytes = 64 * 1024 * 1024; // 64MB

    public static string GetModelCacheDir()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "qmd", "models");
    }
}
