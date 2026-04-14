using Qmd.Core.Models;

namespace Qmd.Core.Indexing;

internal interface ICollectionReindexerService
{
    Task<ReindexResult> ReindexCollectionAsync(string collectionPath, string globPattern,
        string collectionName, ReindexOptions? options = null);
}
