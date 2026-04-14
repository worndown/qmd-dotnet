using Qmd.Core.Models;

namespace Qmd.Core.Embedding;

internal interface IEmbeddingPipelineService
{
    Task<EmbedResult> GenerateEmbeddingsAsync(EmbedPipelineOptions? options = null, Action<int>? ensureVecTable = null);
}
