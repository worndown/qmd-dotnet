namespace Qmd.Core.Tests.TestHelpers;

using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;

internal static class TestDbHelper
{
    /// <summary>
    /// Create an initialized in-memory SQLite database with schema and vec extension.
    /// </summary>
    public static IQmdDatabase CreateInMemoryDb()
    {
        var db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(db);
        VecExtension.TryLoad(db);
        return db;
    }

    /// <summary>
    /// Insert a document with content into the database.
    /// </summary>
    public static void SeedDocument(IQmdDatabase db, string collection, string path,
        string content, string? title = null)
    {
        var hash = ContentHasher.HashContent(content);
        var now = "2025-01-01T00:00:00Z";
        ContentHasher.InsertContent(db, hash, content, now);
        title ??= TitleExtractor.ExtractTitle(content, path);
        DocumentOperations.InsertDocument(db, collection, path, title, hash, now, now);
    }

    /// <summary>
    /// Insert a collection into store_collections.
    /// </summary>
    public static void SeedCollection(IQmdDatabase db, string name, string path,
        string? context = null)
    {
        db.Prepare("INSERT OR REPLACE INTO store_collections (name, path, context) VALUES ($1, $2, $3)")
            .Run(name, path, context ?? (object)DBNull.Value);
    }
}
