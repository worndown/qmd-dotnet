using Qmd.Core.Models;

namespace Qmd.Core.Search;

internal interface IQueryExpanderService
{
    Task<List<ExpandedQuery>> ExpandQueryAsync(string query, string? model, string? intent, CancellationToken ct);
}
