namespace Qmd.Core.Documents;

internal interface IDocumentRepository
{
    void InsertDocument(string collection, string path, string title, string hash, string createdAt, string modifiedAt);
    ActiveDocumentRow? FindActiveDocument(string collection, string path);
    void UpdateDocument(long id, string title, string hash, string modifiedAt);
    void UpdateDocumentTitle(long id, string title, string modifiedAt);
    void DeactivateDocument(string collection, string path);
    List<string> GetActiveDocumentPaths(string collection);
    void InsertContent(string hash, string content, string createdAt);
}
