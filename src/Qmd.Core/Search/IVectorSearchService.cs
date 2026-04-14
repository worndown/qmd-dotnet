using Qmd.Core.Models;

namespace Qmd.Core.Search;

internal interface IVectorSearchService
{
    Task<List<SearchResult>> SearchAsync(string query, string model,
        int limit = 20, List<string>? collections = null,
        float[]? precomputedEmbedding = null, CancellationToken ct = default);
}
