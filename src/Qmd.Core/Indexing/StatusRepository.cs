using Qmd.Core.Database;
using Qmd.Core.Models;

namespace Qmd.Core.Indexing;

internal class StatusRepository : IStatusRepository
{
    private readonly IQmdDatabase _db;

    public StatusRepository(IQmdDatabase db)
    {
        _db = db;
    }

    public IndexStatus GetStatus()
    {
        var totalRow = _db.Prepare("SELECT COUNT(*) as cnt FROM documents WHERE active = 1").Get<CountRow>();
        var total = totalRow?.Cnt ?? 0;

        var needsEmbed = GetHashesNeedingEmbedding();
        var hasVec = VecExtension.IsAvailable;

        var collections = _db.Prepare(@"
            SELECT sc.name, sc.path, sc.pattern,
                   (SELECT COUNT(*) FROM documents d WHERE d.collection = sc.name AND d.active = 1) as doc_count,
                   COALESCE((SELECT MAX(d.modified_at) FROM documents d WHERE d.collection = sc.name AND d.active = 1), '') as last_updated
            FROM store_collections sc
            ORDER BY last_updated DESC, sc.name
        ").All<StatusCollectionRow>();

        var collectionInfos = collections.Select(r => new CollectionInfo(
            r.Name,
            r.Path,
            r.Pattern,
            r.DocCount,
            r.LastUpdated
        )).ToList();

        return new IndexStatus(total, needsEmbed, hasVec, collectionInfos);
    }

    public int GetHashesNeedingEmbedding()
    {
        var row = _db.Prepare(@"
            SELECT COUNT(DISTINCT c.hash) as cnt
            FROM content c
            JOIN documents d ON d.hash = c.hash AND d.active = 1
            LEFT JOIN content_vectors cv ON cv.hash = c.hash AND cv.seq = 0
            WHERE cv.hash IS NULL
        ").Get<CountRow>();
        return row?.Cnt ?? 0;
    }

    public IndexHealthInfo GetIndexHealth()
    {
        var needsEmbed = GetHashesNeedingEmbedding();
        var totalRow = _db.Prepare("SELECT COUNT(*) as cnt FROM documents WHERE active = 1").Get<CountRow>();
        var total = totalRow?.Cnt ?? 0;

        int? daysStale = null;
        var latestRow = _db.Prepare("SELECT MAX(modified_at) as value FROM documents WHERE active = 1").Get<SingleValueRow>();
        if (latestRow?.Value is string latest && DateTime.TryParse(latest, out var latestDate))
        {
            daysStale = (int)(DateTime.UtcNow - latestDate).TotalDays;
        }

        return new IndexHealthInfo(needsEmbed, total, daysStale);
    }
}
