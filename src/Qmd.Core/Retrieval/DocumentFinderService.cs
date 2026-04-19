using System.Text.RegularExpressions;
using Qmd.Core.Database;
using Qmd.Core.Models;
using Qmd.Core.Paths;

namespace Qmd.Core.Retrieval;

/// <summary>
/// Multi-strategy document lookup (instance version with constructor injection).
/// </summary>
internal class DocumentFinderService : IDocumentFinderService
{
    private static readonly Regex ColonLineRegex = new(@":(\d+)$", RegexOptions.Compiled);

    private readonly IQmdDatabase db;
    private readonly IFuzzyMatcherService fuzzyMatcher;
    private readonly IContextResolverService contextResolver;

    public DocumentFinderService(IQmdDatabase db, IFuzzyMatcherService fuzzyMatcher, IContextResolverService contextResolver)
    {
        this.db = db;
        this.fuzzyMatcher = fuzzyMatcher;
        this.contextResolver = contextResolver;
    }

    /// <summary>
    /// Find a document by filename, docid, virtual path, absolute path, or relative path.
    /// Returns DocumentResult or DocumentNotFound with similar file suggestions.
    /// </summary>
    public FindDocumentResult FindDocument(string filename, bool includeBody = false, int similarFilesLimit = 5)
    {
        var filepath = filename;

        // Strip :linenum suffix
        var colonMatch = ColonLineRegex.Match(filepath);
        if (colonMatch.Success)
            filepath = filepath[..^colonMatch.Length];

        // DocId lookup (#abc123, abc123, etc.)
        if (DocidUtils.IsDocid(filepath))
        {
            var docidMatch = this.FindDocumentByDocid(filepath);
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
        var doc = this.db.Prepare($@"
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
            doc = this.db.Prepare($@"
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
            var collections = this.db.Prepare("SELECT name, path FROM store_collections").All<StoreCollectionRow>();
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
                    doc = this.db.Prepare($@"
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
            var similar = this.fuzzyMatcher.FindSimilarFiles(filepath, 5, similarFilesLimit);
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
            DocId = DocidUtils.GetDocid(hash),
            CollectionName = doc.Collection,
            ModifiedAt = doc.ModifiedAt,
            BodyLength = doc.BodyLength,
            Body = includeBody ? doc.Body : null,
            Context = this.contextResolver.GetContextForFile(virtualPath),
        };

        return FindDocumentResult.Found(result);
    }

    /// <summary>
    /// Get document body with optional line slicing.
    /// </summary>
    public string? GetDocumentBody(string filepath, int? fromLine = null, int? maxLines = null)
    {
        BodyRow? row = null;

        // Try virtual path
        if (filepath.StartsWith("qmd://"))
        {
            row = this.db.Prepare(@"
                SELECT content.doc as body
                FROM documents d
                JOIN content ON content.hash = d.hash
                WHERE 'qmd://' || d.collection || '/' || d.path = $1 AND d.active = 1
            ").Get<BodyRow>(filepath);
        }

        // Try absolute path via collections
        if (row == null)
        {
            var collections = this.db.Prepare("SELECT name, path FROM store_collections").All<StoreCollectionRow>();
            foreach (var coll in collections)
            {
                var collPath = coll.Path ?? "";
                if (filepath.StartsWith(collPath + "/"))
                {
                    var relativePath = filepath[(collPath.Length + 1)..];
                    row = this.db.Prepare(@"
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

    private (string Filepath, string Hash)? FindDocumentByDocid(string docid)
    {
        var normalized = DocidUtils.Normalize(docid);
        if (normalized.Length < 1) return null;

        var row = this.db.Prepare(@"
            SELECT 'qmd://' || d.collection || '/' || d.path as filepath, d.hash
            FROM documents d
            WHERE d.hash LIKE $1 AND d.active = 1
            LIMIT 1
        ").Get<DocidRow>($"{normalized}%");

        if (row == null) return null;
        return (row.Filepath, row.Hash);
    }
}
