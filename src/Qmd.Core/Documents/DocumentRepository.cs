using Qmd.Core.Database;

namespace Qmd.Core.Documents;

internal class DocumentRepository : IDocumentRepository
{
    private readonly IQmdDatabase _db;

    public DocumentRepository(IQmdDatabase db)
    {
        _db = db;
    }

    public void InsertDocument(string collection, string path,
        string title, string hash, string createdAt, string modifiedAt)
    {
        // Two-step upsert: UPDATE first, INSERT if no existing row.
        // Single atomic INSERT ... ON CONFLICT DO UPDATE conflicts with FTS triggers.
        var result = _db.Prepare(@"
            UPDATE documents SET title = $1, hash = $2, modified_at = $3, active = 1
            WHERE collection = $4 AND path = $5
        ").Run(title, hash, modifiedAt, collection, path);

        if (result.Changes == 0)
        {
            _db.Prepare(@"
                INSERT INTO documents (collection, path, title, hash, created_at, modified_at, active)
                VALUES ($1, $2, $3, $4, $5, $6, 1)
            ").Run(collection, path, title, hash, createdAt, modifiedAt);
        }
    }

    public ActiveDocumentRow? FindActiveDocument(string collection, string path)
    {
        return _db.Prepare("SELECT id, hash, title FROM documents WHERE collection = $1 AND path = $2 AND active = 1")
            .Get<ActiveDocumentRow>(collection, path);
    }

    public void UpdateDocumentTitle(long id, string title, string modifiedAt)
    {
        _db.Prepare("UPDATE documents SET title = $1, modified_at = $2 WHERE id = $3")
            .Run(title, modifiedAt, id);
    }

    public void UpdateDocument(long id, string title, string hash, string modifiedAt)
    {
        _db.Prepare("UPDATE documents SET title = $1, hash = $2, modified_at = $3 WHERE id = $4")
            .Run(title, hash, modifiedAt, id);
    }

    public void DeactivateDocument(string collection, string path)
    {
        _db.Prepare("UPDATE documents SET active = 0 WHERE collection = $1 AND path = $2 AND active = 1")
            .Run(collection, path);
    }

    public List<string> GetActiveDocumentPaths(string collection)
    {
        var rows = _db.Prepare("SELECT path FROM documents WHERE collection = $1 AND active = 1")
            .All<SinglePathRow>(collection);
        return rows.Select(r => r.Path).ToList();
    }

    public void InsertContent(string hash, string content, string createdAt)
    {
        _db.Prepare("INSERT OR IGNORE INTO content (hash, doc, created_at) VALUES ($1, $2, $3)")
            .Run(hash, content, createdAt);
    }
}
