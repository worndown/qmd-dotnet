namespace Qmd.Core.Llm;

/// <summary>
/// Factory for creating and managing LLM service instances and model files.
/// SDK consumers use this class to obtain an <see cref="ILlmService"/> for
/// passing to <see cref="QmdStoreFactory"/> and for downloading model files.
/// </summary>
public static class LlmServiceFactory
{
    /// <summary>Default HuggingFace URI for the embedding model.</summary>
    public static string DefaultEmbedModel => LlmConstants.DefaultEmbedModel;

    /// <summary>Default HuggingFace URI for the reranking model.</summary>
    public static string DefaultRerankModel => LlmConstants.DefaultRerankModel;

    /// <summary>Default HuggingFace URI for the query-expansion model.</summary>
    public static string DefaultGenerateModel => LlmConstants.DefaultGenerateModel;

    /// <summary>
    /// Create the default <see cref="ILlmService"/> backed by LLamaSharp.
    /// Models are loaded lazily on first use; this call returns immediately.
    /// </summary>
    /// <param name="options">
    /// Model URIs, cache directory, and context size overrides.
    /// Pass <c>null</c> to use defaults (with env-var fallbacks).
    /// </param>
    public static ILlmService Create(LlamaSharpOptions? options = null) =>
        new LlamaSharpService(options);

    /// <summary>
    /// Resolve a model URI to a local file path, downloading from HuggingFace if needed.
    /// Accepts <c>hf:user/repo/file.gguf</c> URIs or absolute local paths.
    /// </summary>
    /// <param name="modelUri">HuggingFace URI or local path.</param>
    /// <param name="force">Re-download even if a cached copy exists.</param>
    /// <param name="onProgress">Optional callback for progress messages.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Absolute path to the local model file.</returns>
    public static Task<string> ResolveModelAsync(
        string modelUri,
        bool force = false,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        var resolver = new ModelResolver();
        return resolver.ResolveModelFileAsync(modelUri, force, onProgress, ct);
    }
}
