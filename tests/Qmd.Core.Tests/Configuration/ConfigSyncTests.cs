using FluentAssertions;
using Qmd.Core.Configuration;
using Qmd.Core.Database;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Configuration;

[Trait("Category", "Database")]
public class ConfigSyncTests : IDisposable
{
    private readonly IQmdDatabase db;
    private readonly ConfigSyncService configSync;

    public ConfigSyncTests()
    {
        this.db = TestDbHelper.CreateInMemoryDb();
        this.configSync = new ConfigSyncService(this.db);
    }

    public void Dispose() => this.db.Dispose();

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

        this.configSync.SyncToDb(config);

        var rows = this.db.Prepare("SELECT name, path FROM store_collections ORDER BY name")
            .All<StoreCollectionRow>();
        rows.Should().HaveCount(2);
        rows[0].Name.Should().Be("docs");
        rows[0].Path.Should().Be("/docs");
        rows[1].Name.Should().Be("notes");
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
        this.configSync.SyncToDb(config1);

        var config2 = new CollectionConfig
        {
            Collections = new()
            {
                ["a"] = new Collection { Path = "/a" },
            }
        };
        this.configSync.SyncToDb(config2);

        var rows = this.db.Prepare("SELECT name FROM store_collections").All<SingleNameRow>();
        rows.Should().HaveCount(1);
        rows[0].Name.Should().Be("a");
    }

    [Fact]
    public void SyncToDb_SyncsGlobalContext()
    {
        var config = new CollectionConfig { GlobalContext = "test context" };
        this.configSync.SyncToDb(config);

        var row = this.db.Prepare("SELECT value FROM store_config WHERE key = $1")
            .Get<SingleValueRow>("global_context");
        row.Should().NotBeNull();
        row!.Value.Should().Be("test context");
    }

    [Fact]
    public void SyncToDb_SkipsWhenUnchanged()
    {
        var config = new CollectionConfig
        {
            Collections = new() { ["a"] = new Collection { Path = "/a" } }
        };

        this.configSync.SyncToDb(config);

        // Second sync should be a no-op (same hash)
        this.configSync.SyncToDb(config);

        var rows = this.db.Prepare("SELECT name FROM store_collections").All<SingleNameRow>();
        rows.Should().HaveCount(1);
    }

    [Fact]
    public void SyncToDb_UpdatesOnChange()
    {
        var config1 = new CollectionConfig
        {
            Collections = new() { ["a"] = new Collection { Path = "/old" } }
        };
        this.configSync.SyncToDb(config1);

        var config2 = new CollectionConfig
        {
            Collections = new() { ["a"] = new Collection { Path = "/new" } }
        };
        this.configSync.SyncToDb(config2);

        var row = this.db.Prepare("SELECT path FROM store_collections WHERE name = $1")
            .Get<SinglePathRow>("a");
        row!.Path.Should().Be("/new");
    }
}
