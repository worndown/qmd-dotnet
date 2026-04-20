using System.Text.RegularExpressions;
using Qmd.Core.Database;
using Qmd.Core.Models;
using Qmd.Core.Paths;

namespace Qmd.Core.Retrieval;

/// <summary>
/// Multi-strategy document lookup.
/// </summary>
internal static class DocumentFinder
{
    private static readonly Regex ColonLineRegex = new(@":(\d+)$", RegexOptions.Compiled);

    /// <summary>
    /// Find a document by filename, docid, virtual path, absolute path, or relative path.
    /// Returns DocumentResult or DocumentNotFound with similar file suggestions.
    /// </summary>
    public static FindDocumentResult FindDocument(IQmdDatabase db, string filename, bool includeBody = false, int similarFilesLimit = 5)
    {
        var filepath = filename;

        // Strip :linenum suffix
        var colonMatch = ColonLineRegex.Match(filepath);
        if (colonMatch.Success)
            filepath = filepath[..^colonMatch.Length];

        // DocId lookup (#abc123, abc123, etc.)
        if (DocIdUtils.IsDocId(filepath))
        {
            var docidMatch = FindDocumentByDocId(db, filepath);
            if (docidMatch != null)
                filepath = docidMatch.Value.Filepath;
            else
                return FindDocumentResult.Missing(filename, []);
        }

        // Home expansion
        if (filepath.StartsWith("~/"))
            filepath = QmdPaths.HomeDir() + filepath[1..];

        var bodyCol = includeBody ? ", content.doc as body" : "";

        // Try virtual path exact match
        var doc = db.Prepare($@"
            SELECT 'qmd://' || d.collection || '/' || d.path as virtual_path,
                   d.collection || '/' || d.path as display_path,
                   d.title, d.hash, d.collection, d.modified_at,
                   LENGTH(content.doc) as body_length {bodyCol}
            FROM documents d
            JOIN content ON content.hash = d.hash
            WHERE 'qmd://' || d.collection || '/' || d.path = $1 AND d.active = 1
        ").Get<DocumentRow>(filepath);

        // Try fuzzy match by virtual path
        if (doc == null)
        {
            doc = db.Prepare($@"
                SELECT 'qmd://' || d.collection || '/' || d.path as virtual_path,
                       d.collection || '/' || d.path as display_path,
                       d.title, d.hash, d.collection, d.modified_at,
                       LENGTH(content.doc) as body_length {bodyCol}
                FROM documents d
                JOIN content ON content.hash = d.hash
                WHERE 'qmd://' || d.collection || '/' || d.path LIKE $1 AND d.active = 1
                LIMIT 1
            ").Get<DocumentRow>($"%{filepath}");
        }

        // Try absolute/relative path via collections
        if (doc == null && !filepath.StartsWith("qmd://"))
        {
            var collections = db.Prepare("SELECT name, path FROM store_collections").All<StoreCollectionRow>();
            foreach (var coll in collections)
            {
                var collName = coll.Name;
                var collPath = coll.Path ?? "";
                string? relativePath = null;

                if (filepath.StartsWith(collPath + "/"))
                    relativePath = filepath[(collPath.Length + 1)..];
                else if (!filepath.StartsWith("/"))
                    relativePath = filepath;

                if (relativePath != null)
                {
                    doc = db.Prepare($@"
                        SELECT 'qmd://' || d.collection || '/' || d.path as virtual_path,
                               d.collection || '/' || d.path as display_path,
                               d.title, d.hash, d.collection, d.modified_at,
                               LENGTH(content.doc) as body_length {bodyCol}
                        FROM documents d
                        JOIN content ON content.hash = d.hash
                        WHERE d.collection = $1 AND d.path = $2 AND d.active = 1
                    ").Get<DocumentRow>(collName, relativePath);
                    if (doc != null) break;
                }
            }
        }

        if (doc == null)
        {
            var similar = FuzzyMatcher.FindSimilarFiles(db, filepath, 5, similarFilesLimit);
            return FindDocumentResult.Missing(filename, similar);
        }

        var hash = doc.Hash;
        var virtualPath = doc.VirtualPath;
        var result = new DocumentResult
        {
            Filepath = virtualPath,
            DisplayPath = doc.DisplayPath,
            Title = doc.Title,
            Hash = hash,
            DocId = DocIdUtils.GetDocId(hash),
            CollectionName = doc.Collection,
            ModifiedAt = doc.ModifiedAt,
            BodyLength = doc.BodyLength,
            Body = includeBody ? doc.Body : null,
            Context = ContextResolver.GetContextForFile(db, virtualPath),
        };

        return FindDocumentResult.Found(result);
    }

    /// <summary>
    /// Get document body with optional line slicing.
    /// </summary>
    public static string? GetDocumentBody(IQmdDatabase db, string filepath, int? fromLine = null, int? maxLines = null)
    {
        BodyRow? row = null;

        // Try virtual path
        if (filepath.StartsWith("qmd://"))
        {
            row = db.Prepare(@"
                SELECT content.doc as body
                FROM documents d
                JOIN content ON content.hash = d.hash
                WHERE 'qmd://' || d.collection || '/' || d.path = $1 AND d.active = 1
            ").Get<BodyRow>(filepath);
        }

        // Try absolute path via collections
        if (row == null)
        {
            var collections = db.Prepare("SELECT name, path FROM store_collections").All<StoreCollectionRow>();
            foreach (var coll in collections)
            {
                var collPath = coll.Path ?? "";
                if (filepath.StartsWith(collPath + "/"))
                {
                    var relativePath = filepath[(collPath.Length + 1)..];
                    row = db.Prepare(@"
                        SELECT content.doc as body
                        FROM documents d
                        JOIN content ON content.hash = d.hash
                        WHERE d.collection = $1 AND d.path = $2 AND d.active = 1
                    ").Get<BodyRow>(coll.Name, relativePath);
                    if (row != null) break;
                }
            }
        }

        if (row == null) return null;

        var body = row.Body ?? "";
        if (fromLine.HasValue || maxLines.HasValue)
        {
            var lines = body.Split('\n');
            var start = (fromLine ?? 1) - 1;
            var end = maxLines.HasValue ? start + maxLines.Value : lines.Length;
            body = string.Join('\n', lines.Skip(start).Take(end - start));
        }

        return body;
    }

    private static (string Filepath, string Hash)? FindDocumentByDocId(IQmdDatabase db, string docid)
    {
        var normalized = DocIdUtils.Normalize(docid);
        if (normalized.Length < 1) return null;

        var row = db.Prepare(@"
            SELECT 'qmd://' || d.collection || '/' || d.path as filepath, d.hash
            FROM documents d
            WHERE d.hash LIKE $1 AND d.active = 1
            LIMIT 1
        ").Get<DocIdRow>($"{normalized}%");

        if (row == null) return null;
        return (row.Filepath, row.Hash);
    }
}
