using Qmd.Core.Models;

namespace Qmd.Core.Retrieval;

internal interface IDocumentFinderService
{
    FindDocumentResult FindDocument(string filename, bool includeBody = false, int similarFilesLimit = 5);
    string? GetDocumentBody(string filepath, int? fromLine = null, int? maxLines = null);
}
