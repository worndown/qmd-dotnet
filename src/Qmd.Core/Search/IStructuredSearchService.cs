using Qmd.Core.Models;

namespace Qmd.Core.Search;

internal interface IStructuredSearchService
{
    Task<List<HybridQueryResult>> SearchAsync(List<ExpandedQuery> searches, StructuredSearchOptions? options, CancellationToken ct);
}
