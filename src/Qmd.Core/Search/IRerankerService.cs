using Qmd.Core.Models;

namespace Qmd.Core.Search;

internal interface IRerankerService
{
    Task<List<(string File, double Score)>> RerankAsync(
        string query, List<RerankDocument> documents,
        string? model, string? intent, CancellationToken ct);
}
