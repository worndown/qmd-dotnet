using FluentAssertions;
using Qmd.Core.Configuration;
using Qmd.Core.Database;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Configuration;

[Trait("Category", "Database")]
public class ConfigSyncTests : IDisposable
{
    private readonly IQmdDatabase _db;
    private readonly ConfigSyncService _configSync;

    public ConfigSyncTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
        _configSync = new ConfigSyncService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void SyncToDb_InsertsCollections()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/docs", Pattern = "**/*.md" },
                ["notes"] = new Collection { Path = "/notes", Pattern = "**/*.txt" },
            }
        };

        _configSync.SyncToDb(config);

        var rows = _db.Prepare("SELECT name, path FROM store_collections ORDER BY name").AllDynamic();
        rows.Should().HaveCount(2);
        rows[0]["name"].Should().Be("docs");
        rows[0]["path"].Should().Be("/docs");
        rows[1]["name"].Should().Be("notes");
    }

    [Fact]
    public void SyncToDb_DeletesRemovedCollections()
    {
        var config1 = new CollectionConfig
        {
            Collections = new()
            {
                ["a"] = new Collection { Path = "/a" },
                ["b"] = new Collection { Path = "/b" },
            }
        };
        _configSync.SyncToDb(config1);

        var config2 = new CollectionConfig
        {
            Collections = new()
            {
                ["a"] = new Collection { Path = "/a" },
            }
        };
        _configSync.SyncToDb(config2);

        var rows = _db.Prepare("SELECT name FROM store_collections").AllDynamic();
        rows.Should().HaveCount(1);
        rows[0]["name"].Should().Be("a");
    }

    [Fact]
    public void SyncToDb_SyncsGlobalContext()
    {
        var config = new CollectionConfig { GlobalContext = "test context" };
        _configSync.SyncToDb(config);

        var row = _db.Prepare("SELECT value FROM store_config WHERE key = $1")
            .GetDynamic("global_context");
        row.Should().NotBeNull();
        row!["value"].Should().Be("test context");
    }

    [Fact]
    public void SyncToDb_SkipsWhenUnchanged()
    {
        var config = new CollectionConfig
        {
            Collections = new() { ["a"] = new Collection { Path = "/a" } }
        };

        _configSync.SyncToDb(config);

        // Second sync should be a no-op (same hash)
        _configSync.SyncToDb(config);

        var rows = _db.Prepare("SELECT name FROM store_collections").AllDynamic();
        rows.Should().HaveCount(1);
    }

    [Fact]
    public void SyncToDb_UpdatesOnChange()
    {
        var config1 = new CollectionConfig
        {
            Collections = new() { ["a"] = new Collection { Path = "/old" } }
        };
        _configSync.SyncToDb(config1);

        var config2 = new CollectionConfig
        {
            Collections = new() { ["a"] = new Collection { Path = "/new" } }
        };
        _configSync.SyncToDb(config2);

        var row = _db.Prepare("SELECT path FROM store_collections WHERE name = $1")
            .GetDynamic("a");
        row!["path"].Should().Be("/new");
    }
}
