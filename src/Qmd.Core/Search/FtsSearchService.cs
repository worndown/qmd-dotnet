using Qmd.Core.Database;
using Qmd.Core.Models;
using Qmd.Core.Paths;
using Qmd.Core.Retrieval;

namespace Qmd.Core.Search;

internal class FtsSearchService : IFtsSearchService
{
    private readonly IQmdDatabase db;

    public FtsSearchService(IQmdDatabase db)
    {
        this.db = db;
    }

    public List<SearchResult> Search(string query, int limit = 20, List<string>? collections = null)
    {
        var ftsQuery = Fts5QueryBuilder.BuildFTS5Query(query);
        if (ftsQuery == null) return [];

        var ftsLimit = collections is { Count: > 0 } ? limit * 10 : limit;

        var sql = $@"
            WITH fts_matches AS (
                SELECT rowid, bm25(documents_fts, 1.5, 4.0, 1.0) as bm25_score
                FROM documents_fts
                WHERE documents_fts MATCH $1
                ORDER BY bm25_score ASC
                LIMIT {ftsLimit}
            )
            SELECT
                'qmd://' || d.collection || '/' || d.path as filepath,
                d.collection || '/' || d.path as display_path,
                d.title,
                content.doc as body,
                d.hash,
                d.collection,
                fm.bm25_score
            FROM fts_matches fm
            JOIN documents d ON d.id = fm.rowid
            JOIN content ON content.hash = d.hash
            WHERE d.active = 1";

        var parameters = new List<object?> { ftsQuery };

        if (collections is { Count: > 0 })
        {
            var placeholders = string.Join(",", collections.Select((_, i) => $"${i + 2}"));
            sql += $" AND d.collection IN ({placeholders})";
            foreach (var c in collections)
                parameters.Add(c);
        }

        sql += " ORDER BY fm.bm25_score ASC LIMIT $" + (parameters.Count + 1);
        parameters.Add((long)limit);

        var rows = this.db.Prepare(sql).All<FtsMatchRow>(parameters.ToArray());

        return rows.Select(row =>
        {
            var score = Math.Abs(row.Bm25Score) / (1 + Math.Abs(row.Bm25Score));
            var hash = row.Hash;
            var body = row.Body ?? "";

            var virtualPath = row.Filepath;
            return new SearchResult
            {
                Filepath = virtualPath,
                DisplayPath = row.DisplayPath,
                Title = row.Title,
                Hash = hash,
                DocId = DocidUtils.GetDocid(hash),
                CollectionName = row.Collection,
                ModifiedAt = "",
                BodyLength = body.Length,
                Body = body,
                Score = score,
                Source = "fts",
                Context = ContextResolver.GetContextForFile(this.db, virtualPath),
            };
        }).ToList();
    }
}
