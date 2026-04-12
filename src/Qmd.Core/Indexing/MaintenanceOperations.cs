using Qmd.Core.Database;

namespace Qmd.Core.Indexing;

internal static class MaintenanceOperations
{
    public static int DeleteInactiveDocuments(IQmdDatabase db)
    {
        return db.Prepare("DELETE FROM documents WHERE active = 0").Run().Changes;
    }

    public static int DeleteOrphanedCollectionDocuments(IQmdDatabase db)
    {
        return db.Prepare(@"
            DELETE FROM documents WHERE collection NOT IN (
                SELECT name FROM store_collections
            )
        ").Run().Changes;
    }

    public static int CleanupOrphanedContent(IQmdDatabase db)
    {
        return db.Prepare(@"
            DELETE FROM content WHERE hash NOT IN (
                SELECT DISTINCT hash FROM documents WHERE active = 1
            )
        ").Run().Changes;
    }

    public static int CleanupOrphanedVectors(IQmdDatabase db)
    {
        // Check if vectors_vec table exists
        var exists = db.Prepare("SELECT name FROM sqlite_master WHERE type='table' AND name='vectors_vec'").GetDynamic();
        if (exists == null) return 0;

        // Check if sqlite-vec is available
        try
        {
            db.Prepare("SELECT 1 FROM vectors_vec LIMIT 0").GetDynamic();
        }
        catch
        {
            return 0; // sqlite-vec not available
        }

        // Count orphaned vectors
        var countRow = db.Prepare(@"
            SELECT COUNT(*) as c FROM content_vectors cv
            WHERE NOT EXISTS (
                SELECT 1 FROM documents d WHERE d.hash = cv.hash AND d.active = 1
            )
        ").GetDynamic();
        var count = Convert.ToInt32(countRow?["c"] ?? 0);
        if (count == 0) return 0;

        // Delete from vectors_vec first (must happen before content_vectors deletion)
        db.Prepare(@"
            DELETE FROM vectors_vec WHERE hash_seq IN (
                SELECT cv.hash || '_' || cv.seq FROM content_vectors cv
                WHERE NOT EXISTS (
                    SELECT 1 FROM documents d WHERE d.hash = cv.hash AND d.active = 1
                )
            )
        ").Run();

        // Delete from content_vectors
        db.Prepare(@"
            DELETE FROM content_vectors WHERE hash NOT IN (
                SELECT hash FROM documents WHERE active = 1
            )
        ").Run();

        return count;
    }

    public static void VacuumDatabase(IQmdDatabase db)
    {
        db.Exec("VACUUM");
    }

    public static int DeleteLLMCache(IQmdDatabase db)
    {
        return db.Prepare("DELETE FROM llm_cache").Run().Changes;
    }
}
