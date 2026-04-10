using Qmd.Core.Database;

namespace Qmd.Core.Documents;

public class ActiveDocumentRow
{
    public long Id { get; set; }
    public string Hash { get; set; } = "";
    public string Title { get; set; } = "";
}

public static class DocumentOperations
{
    public static void InsertDocument(IQmdDatabase db, string collection, string path,
        string title, string hash, string createdAt, string modifiedAt)
    {
        // Two-step upsert: UPDATE first, INSERT if no existing row.
        // Single atomic INSERT ... ON CONFLICT DO UPDATE conflicts with FTS triggers.
        var result = db.Prepare(@"
            UPDATE documents SET title = $1, hash = $2, modified_at = $3, active = 1
            WHERE collection = $4 AND path = $5
        ").Run(title, hash, modifiedAt, collection, path);

        if (result.Changes == 0)
        {
            db.Prepare(@"
                INSERT INTO documents (collection, path, title, hash, created_at, modified_at, active)
                VALUES ($1, $2, $3, $4, $5, $6, 1)
            ").Run(collection, path, title, hash, createdAt, modifiedAt);
        }
    }

    public static ActiveDocumentRow? FindActiveDocument(IQmdDatabase db, string collection, string path)
    {
        return db.Prepare("SELECT id, hash, title FROM documents WHERE collection = $1 AND path = $2 AND active = 1")
            .Get<ActiveDocumentRow>(collection, path);
    }

    public static void UpdateDocumentTitle(IQmdDatabase db, long id, string title, string modifiedAt)
    {
        db.Prepare("UPDATE documents SET title = $1, modified_at = $2 WHERE id = $3")
            .Run(title, modifiedAt, id);
    }

    public static void UpdateDocument(IQmdDatabase db, long id, string title, string hash, string modifiedAt)
    {
        db.Prepare("UPDATE documents SET title = $1, hash = $2, modified_at = $3 WHERE id = $4")
            .Run(title, hash, modifiedAt, id);
    }

    public static void DeactivateDocument(IQmdDatabase db, string collection, string path)
    {
        db.Prepare("UPDATE documents SET active = 0 WHERE collection = $1 AND path = $2 AND active = 1")
            .Run(collection, path);
    }

    public static List<string> GetActiveDocumentPaths(IQmdDatabase db, string collection)
    {
        var rows = db.Prepare("SELECT path FROM documents WHERE collection = $1 AND active = 1")
            .AllDynamic(collection);
        return rows.Select(r => r["path"]!.ToString()!).ToList();
    }
}
