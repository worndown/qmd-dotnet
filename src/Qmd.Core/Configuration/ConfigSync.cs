using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Qmd.Core.Database;

namespace Qmd.Core.Configuration;

/// <summary>
/// One-way sync from YAML config to SQLite database.
/// </summary>
public static class ConfigSync
{
    public static void SyncToDb(IQmdDatabase db, CollectionConfig config)
    {
        // Compute hash of config
        var configJson = JsonSerializer.Serialize(config);
        var hash = ComputeHash(configJson);

        // Check if hash matches stored hash
        var storedHash = db.Prepare("SELECT value FROM store_config WHERE key = $1")
            .GetDynamic("config_hash");
        if (storedHash != null && storedHash["value"]?.ToString() == hash)
            return; // Config unchanged

        // Sync collections
        var configNames = new HashSet<string>(config.Collections.Keys);

        foreach (var (name, coll) in config.Collections)
        {
            var ignoreJson = coll.Ignore != null ? JsonSerializer.Serialize(coll.Ignore) : null;
            var contextJson = coll.Context != null ? JsonSerializer.Serialize(coll.Context) : null;

            db.Prepare(@"
                INSERT INTO store_collections (name, path, pattern, ignore_patterns, include_by_default, update_command, context)
                VALUES ($1, $2, $3, $4, $5, $6, $7)
                ON CONFLICT(name) DO UPDATE SET
                    path = excluded.path,
                    pattern = excluded.pattern,
                    ignore_patterns = excluded.ignore_patterns,
                    include_by_default = excluded.include_by_default,
                    update_command = excluded.update_command,
                    context = excluded.context
            ").Run(
                name,
                coll.Path,
                coll.Pattern,
                ignoreJson,
                coll.IncludeByDefault != false ? 1L : 0L,
                coll.Update,
                contextJson
            );
        }

        // Delete collections not in config
        var dbCollections = db.Prepare("SELECT name FROM store_collections").AllDynamic();
        foreach (var row in dbCollections)
        {
            var name = row["name"]?.ToString();
            if (name != null && !configNames.Contains(name))
            {
                db.Prepare("DELETE FROM store_collections WHERE name = $1").Run(name);
            }
        }

        // Sync global context (delete row when null)
        if (config.GlobalContext != null)
        {
            db.Prepare(@"
                INSERT INTO store_config (key, value) VALUES ($1, $2)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value
            ").Run("global_context", config.GlobalContext);
        }
        else
        {
            db.Prepare("DELETE FROM store_config WHERE key = $1").Run("global_context");
        }

        // Store hash
        db.Prepare(@"
            INSERT INTO store_config (key, value) VALUES ($1, $2)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
        ").Run("config_hash", hash);
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
