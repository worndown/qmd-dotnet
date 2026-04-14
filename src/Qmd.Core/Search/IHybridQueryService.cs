using Qmd.Core.Models;

namespace Qmd.Core.Search;

internal interface IHybridQueryService
{
    Task<List<HybridQueryResult>> HybridQueryAsync(string query, HybridQueryOptions? options, CancellationToken ct);
}
