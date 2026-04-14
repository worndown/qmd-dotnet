using Qmd.Core.Database;

namespace Qmd.Core.Retrieval;

internal class FuzzyMatcherService : IFuzzyMatcherService
{
    private readonly IQmdDatabase _db;

    public FuzzyMatcherService(IQmdDatabase db)
    {
        _db = db;
    }

    public List<string> FindSimilarFiles(string query, int maxDistance = 3, int limit = 5) =>
        FuzzyMatcher.FindSimilarFiles(_db, query, maxDistance, limit);
}
