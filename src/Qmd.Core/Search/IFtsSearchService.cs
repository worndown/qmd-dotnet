using Qmd.Core.Models;

namespace Qmd.Core.Search;

internal interface IFtsSearchService
{
    List<SearchResult> Search(string query, int limit = 20, List<string>? collections = null);
}
