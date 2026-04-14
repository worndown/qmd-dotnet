namespace Qmd.Core.Retrieval;

internal interface IContextResolverService
{
    string? GetContextForFile(string filepath);
}
