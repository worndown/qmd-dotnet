using Qmd.Core.Models;

namespace Qmd.Core.Retrieval;

internal interface IMultiGetService
{
    (List<MultiGetResult> Docs, List<string> Errors) FindDocuments(string pattern, bool includeBody = false, int maxBytes = 10 * 1024);
}
