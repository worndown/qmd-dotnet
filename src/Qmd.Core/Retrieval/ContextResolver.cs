using System.Text.Json;
using Qmd.Core.Database;
using Qmd.Core.Paths;

namespace Qmd.Core.Retrieval;

/// <summary>
/// Resolves context strings for document files by matching global context
/// and all applicable path prefix contexts.
/// </summary>
internal static class ContextResolver
{
    /// <summary>
    /// Get the combined context for a file (global + all matching path prefixes, general to specific).
    /// Returns null if no context applies.
    /// </summary>
    public static string? GetContextForFile(IQmdDatabase db, string filepath)
    {
        if (string.IsNullOrEmpty(filepath)) return null;

        // Parse virtual path or resolve absolute path to collection + relative path
        string? collectionName = null;
        string? relativePath = null;

        if (filepath.StartsWith("qmd://"))
        {
            var parsed = VirtualPaths.Parse(filepath);
            if (parsed == null) return null;
            collectionName = parsed.CollectionName;
            relativePath = parsed.Path;
        }
        else
        {
            // Filesystem path: find which collection owns this path
            var collections = db.Prepare("SELECT name, path FROM store_collections").AllDynamic();
            foreach (var coll in collections)
            {
                var collPath = coll["path"]?.ToString();
                if (string.IsNullOrEmpty(collPath)) continue;

                if (filepath.StartsWith(collPath + "/") || filepath == collPath)
                {
                    collectionName = coll["name"]!.ToString();
                    relativePath = filepath.StartsWith(collPath + "/")
                        ? filepath[(collPath.Length + 1)..]
                        : "";
                    break;
                }
            }
        }

        if (collectionName == null || relativePath == null) return null;

        // Get collection's context JSON from DB
        var collRow = db.Prepare("SELECT context FROM store_collections WHERE name = $1")
            .GetDynamic(collectionName);
        if (collRow == null) return null;

        // Verify document exists in DB
        var docCheck = db.Prepare(
            "SELECT 1 FROM documents WHERE collection = $1 AND path = $2 AND active = 1 LIMIT 1")
            .GetDynamic(collectionName, relativePath);
        if (docCheck == null) return null;

        // Collect all matching contexts (global + path prefixes)
        var contexts = new List<string>();

        // Global context
        var globalRow = db.Prepare("SELECT value FROM store_config WHERE key = $1")
            .GetDynamic("global_context");
        var globalCtx = globalRow?["value"]?.ToString();
        if (!string.IsNullOrEmpty(globalCtx))
            contexts.Add(globalCtx);

        // Path prefix contexts from collection
        var contextJson = collRow["context"]?.ToString();
        if (!string.IsNullOrEmpty(contextJson))
        {
            var pathContexts = JsonSerializer.Deserialize<Dictionary<string, string>>(contextJson);
            if (pathContexts != null)
            {
                var normalizedPath = relativePath.StartsWith("/") ? relativePath : $"/{relativePath}";

                var matching = pathContexts
                    .Select(kv =>
                    {
                        var normalizedPrefix = kv.Key.StartsWith("/") ? kv.Key : $"/{kv.Key}";
                        return (Prefix: normalizedPrefix, Context: kv.Value);
                    })
                    .Where(x => normalizedPath.StartsWith(x.Prefix))
                    .OrderBy(x => x.Prefix.Length) // most general first
                    .ToList();

                foreach (var match in matching)
                    contexts.Add(match.Context);
            }
        }

        return contexts.Count > 0 ? string.Join("\n\n", contexts) : null;
    }
}
