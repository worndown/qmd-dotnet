using System.Text.Json;
using Qmd.Core.Database;

namespace Qmd.Core.Search;

internal static class SearchConfigRepository
{
    private const string Key = "search_config";

    public static SearchConfig Load(IQmdDatabase db)
    {
        var row = db.Prepare("SELECT value FROM store_config WHERE key = $1")
            .Get<SingleValueRow>(Key);
        if (row?.Value == null) return new SearchConfig();

        try
        {
            return JsonSerializer.Deserialize<SearchConfig>(row.Value) ?? new SearchConfig();
        }
        catch
        {
            return new SearchConfig();
        }
    }

    public static void Save(IQmdDatabase db, SearchConfig config)
    {
        var json = JsonSerializer.Serialize(config);
        db.Prepare(@"INSERT INTO store_config (key, value) VALUES ($1, $2)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value")
            .Run(Key, json);
    }

    public static void Delete(IQmdDatabase db)
    {
        db.Prepare("DELETE FROM store_config WHERE key = $1").Run(Key);
    }
}
