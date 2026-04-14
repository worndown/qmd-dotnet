namespace Qmd.Core.Indexing;

internal interface ICacheRepository
{
    string? GetCachedResult(string cacheKey);
    void SetCachedResult(string cacheKey, string result);
    void ClearCache();
}
