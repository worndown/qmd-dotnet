using Qmd.Core.Database;

namespace Qmd.Core.Retrieval;

internal class FuzzyMatcherService : IFuzzyMatcherService
{
    private readonly IQmdDatabase db;

    public FuzzyMatcherService(IQmdDatabase db)
    {
        this.db = db;
    }

    public List<string> FindSimilarFiles(string query, int maxDistance = 3, int limit = 5) =>
        FuzzyMatcher.FindSimilarFiles(this.db, query, maxDistance, limit);
}
