namespace Qmd.Core.Retrieval;

internal interface IFuzzyMatcherService
{
    List<string> FindSimilarFiles(string query, int maxDistance = 3, int limit = 5);
}
