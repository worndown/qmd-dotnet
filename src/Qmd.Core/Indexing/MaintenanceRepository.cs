using Microsoft.Data.Sqlite;
using Qmd.Core.Database;

namespace Qmd.Core.Indexing;

internal class MaintenanceRepository : IMaintenanceRepository
{
    private readonly IQmdDatabase _db;

    public MaintenanceRepository(IQmdDatabase db)
    {
        _db = db;
    }

    public int DeleteInactiveDocuments()
    {
        return _db.Prepare("DELETE FROM documents WHERE active = 0").Run().Changes;
    }

    public int DeleteOrphanedCollectionDocuments()
    {
        return _db.Prepare(@"
            DELETE FROM documents WHERE collection NOT IN (
                SELECT name FROM store_collections
            )
        ").Run().Changes;
    }

    public int CleanupOrphanedContent()
    {
        return _db.Prepare(@"
            DELETE FROM content WHERE hash NOT IN (
                SELECT DISTINCT hash FROM documents WHERE active = 1
            )
        ").Run().Changes;
    }

    public int CleanupOrphanedVectors()
    {
        // Check if vectors_vec table exists
        var exists = _db.Prepare("SELECT name FROM sqlite_master WHERE type='table' AND name='vectors_vec'").Get<SqliteMasterRow>();
        if (exists == null) return 0;

        // Check if sqlite-vec is available
        try
        {
            _db.Prepare("SELECT 1 FROM vectors_vec LIMIT 0").Get<SqliteMasterRow>();
        }
        catch (SqliteException)
        {
            return 0; // sqlite-vec not available
        }

        // Count orphaned vectors
        var countRow = _db.Prepare(@"
            SELECT COUNT(*) as cnt FROM content_vectors cv
            WHERE NOT EXISTS (
                SELECT 1 FROM documents d WHERE d.hash = cv.hash AND d.active = 1
            )
        ").Get<CountRow>();
        var count = countRow?.Cnt ?? 0;
        if (count == 0) return 0;

        // Delete from vectors_vec first (must happen before content_vectors deletion)
        _db.Prepare(@"
            DELETE FROM vectors_vec WHERE hash_seq IN (
                SELECT cv.hash || '_' || cv.seq FROM content_vectors cv
                WHERE NOT EXISTS (
                    SELECT 1 FROM documents d WHERE d.hash = cv.hash AND d.active = 1
                )
            )
        ").Run();

        // Delete from content_vectors
        _db.Prepare(@"
            DELETE FROM content_vectors WHERE hash NOT IN (
                SELECT hash FROM documents WHERE active = 1
            )
        ").Run();

        return count;
    }

    public void VacuumDatabase()
    {
        _db.Exec("VACUUM");
    }

    public int DeleteLLMCache()
    {
        return _db.Prepare("DELETE FROM llm_cache").Run().Changes;
    }
}
