using Qmd.Core.Database;

namespace Qmd.Core.Retrieval;

internal class ContextResolverService : IContextResolverService
{
    private readonly IQmdDatabase _db;

    public ContextResolverService(IQmdDatabase db)
    {
        _db = db;
    }

    public string? GetContextForFile(string filepath) =>
        ContextResolver.GetContextForFile(_db, filepath);
}
