using Qmd.Core.Database;
using Qmd.Core.Models;

namespace Qmd.Core.Indexing;

internal static class StatusOperations
{
    public static IndexStatus GetStatus(IQmdDatabase db)
    {
        var totalRow = db.Prepare("SELECT COUNT(*) as cnt FROM documents WHERE active = 1").GetDynamic();
        var total = Convert.ToInt32(totalRow?["cnt"] ?? 0L);

        var needsEmbed = GetHashesNeedingEmbedding(db);
        var hasVec = VecExtension.IsAvailable;

        var collections = db.Prepare(@"
            SELECT sc.name, sc.path, sc.pattern,
                   (SELECT COUNT(*) FROM documents d WHERE d.collection = sc.name AND d.active = 1) as doc_count,
                   COALESCE((SELECT MAX(d.modified_at) FROM documents d WHERE d.collection = sc.name AND d.active = 1), '') as last_updated
            FROM store_collections sc
            ORDER BY last_updated DESC, sc.name
        ").AllDynamic();

        var collectionInfos = collections.Select(r => new CollectionInfo(
            r["name"]!.ToString()!,
            r["path"]?.ToString(),
            r["pattern"]?.ToString(),
            Convert.ToInt32(r["doc_count"] ?? 0L),
            r["last_updated"]?.ToString() ?? ""
        )).ToList();

        return new IndexStatus(total, needsEmbed, hasVec, collectionInfos);
    }

    public static int GetHashesNeedingEmbedding(IQmdDatabase db)
    {
        var row = db.Prepare(@"
            SELECT COUNT(DISTINCT c.hash) as cnt
            FROM content c
            JOIN documents d ON d.hash = c.hash AND d.active = 1
            LEFT JOIN content_vectors cv ON cv.hash = c.hash AND cv.seq = 0
            WHERE cv.hash IS NULL
        ").GetDynamic();
        return Convert.ToInt32(row?["cnt"] ?? 0L);
    }

    public static IndexHealthInfo GetIndexHealth(IQmdDatabase db)
    {
        var needsEmbed = GetHashesNeedingEmbedding(db);
        var totalRow = db.Prepare("SELECT COUNT(*) as cnt FROM documents WHERE active = 1").GetDynamic();
        var total = Convert.ToInt32(totalRow?["cnt"] ?? 0L);

        int? daysStale = null;
        var latestRow = db.Prepare("SELECT MAX(modified_at) as latest FROM documents WHERE active = 1").GetDynamic();
        if (latestRow?["latest"] is string latest && DateTime.TryParse(latest, out var latestDate))
        {
            daysStale = (int)(DateTime.UtcNow - latestDate).TotalDays;
        }

        return new IndexHealthInfo(needsEmbed, total, daysStale);
    }
}
