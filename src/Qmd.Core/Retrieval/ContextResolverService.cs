using Qmd.Core.Database;

namespace Qmd.Core.Retrieval;

internal class ContextResolverService : IContextResolverService
{
    private readonly IQmdDatabase db;

    public ContextResolverService(IQmdDatabase db)
    {
        this.db = db;
    }

    public string? GetContextForFile(string filepath) =>
        ContextResolver.GetContextForFile(this.db, filepath);
}
