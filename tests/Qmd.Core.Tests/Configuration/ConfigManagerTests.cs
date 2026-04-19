using FluentAssertions;
using Qmd.Core.Configuration;

namespace Qmd.Core.Tests.Configuration;

[Trait("Category", "Unit")]
public class ConfigManagerTests
{
    private ConfigManager CreateInlineManager(CollectionConfig? config = null)
    {
        var source = new InlineConfigSource(config);
        return new ConfigManager(source);
    }

    [Fact]
    public void AddCollection_CreatesNew()
    {
        var mgr = this.CreateInlineManager();
        mgr.AddCollection("docs", "/home/docs", "**/*.md");

        var coll = mgr.GetCollection("docs");
        coll.Should().NotBeNull();
        coll!.Name.Should().Be("docs");
        coll.Path.Should().Be("/home/docs");
        coll.Pattern.Should().Be("**/*.md");
    }

    [Fact]
    public void AddCollection_UpdatesExisting()
    {
        var mgr = this.CreateInlineManager();
        mgr.AddCollection("docs", "/old/path");
        mgr.AddCollection("docs", "/new/path", "**/*.txt");

        var coll = mgr.GetCollection("docs");
        coll!.Path.Should().Be("/new/path");
        coll.Pattern.Should().Be("**/*.txt");
    }

    [Fact]
    public void RemoveCollection_ReturnsTrue()
    {
        var mgr = this.CreateInlineManager();
        mgr.AddCollection("docs", "/path");
        mgr.RemoveCollection("docs").Should().BeTrue();
        mgr.GetCollection("docs").Should().BeNull();
    }

    [Fact]
    public void RemoveCollection_ReturnsFalse_WhenNotFound()
    {
        var mgr = this.CreateInlineManager();
        mgr.RemoveCollection("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void RenameCollection_Success()
    {
        var mgr = this.CreateInlineManager();
        mgr.AddCollection("old", "/path");
        mgr.RenameCollection("old", "new").Should().BeTrue();
        mgr.GetCollection("old").Should().BeNull();
        mgr.GetCollection("new").Should().NotBeNull();
        mgr.GetCollection("new")!.Path.Should().Be("/path");
    }

    [Fact]
    public void RenameCollection_ThrowsIfNewExists()
    {
        var mgr = this.CreateInlineManager();
        mgr.AddCollection("a", "/path1");
        mgr.AddCollection("b", "/path2");
        var act = () => mgr.RenameCollection("a", "b");
        act.Should().Throw<QmdException>().WithMessage("*already exists*");
    }

    [Fact]
    public void ListCollections_ReturnsAll()
    {
        var mgr = this.CreateInlineManager();
        mgr.AddCollection("a", "/a");
        mgr.AddCollection("b", "/b");
        mgr.ListCollections().Should().HaveCount(2);
    }

    [Fact]
    public void GetDefaultCollections_ExcludesNonDefault()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["a"] = new Collection { Path = "/a" },
                ["b"] = new Collection { Path = "/b", IncludeByDefault = false },
            }
        };
        var mgr = this.CreateInlineManager(config);
        var defaults = mgr.GetDefaultCollectionNames();
        defaults.Should().Contain("a");
        defaults.Should().NotContain("b");
    }

    [Fact]
    public void GlobalContext_SetAndGet()
    {
        var mgr = this.CreateInlineManager();
        mgr.SetGlobalContext("global info");
        mgr.GetGlobalContext().Should().Be("global info");
    }

    [Fact]
    public void GlobalContext_SetNull_Clears()
    {
        var mgr = this.CreateInlineManager();
        mgr.SetGlobalContext("info");
        mgr.SetGlobalContext(null);
        mgr.GetGlobalContext().Should().BeNull();
    }

    [Fact]
    public void AddContext_Success()
    {
        var mgr = this.CreateInlineManager();
        mgr.AddCollection("docs", "/docs");
        mgr.AddContext("docs", "/guides", "Guide section").Should().BeTrue();

        var ctxs = mgr.GetContexts("docs");
        ctxs.Should().ContainKey("/guides");
        ctxs!["/guides"].Should().Be("Guide section");
    }

    [Fact]
    public void AddContext_ReturnsFalse_WhenCollectionMissing()
    {
        var mgr = this.CreateInlineManager();
        mgr.AddContext("nonexistent", "/", "text").Should().BeFalse();
    }

    [Fact]
    public void RemoveContext_Success()
    {
        var mgr = this.CreateInlineManager();
        mgr.AddCollection("docs", "/docs");
        mgr.AddContext("docs", "/guides", "Guide section");
        mgr.RemoveContext("docs", "/guides").Should().BeTrue();
        mgr.GetContexts("docs").Should().BeNull(); // Empty context map removed
    }

    [Fact]
    public void ListAllContexts_IncludesGlobalAndCollection()
    {
        var mgr = this.CreateInlineManager();
        mgr.SetGlobalContext("global");
        mgr.AddCollection("docs", "/docs");
        mgr.AddContext("docs", "/api", "API docs");

        var all = mgr.ListAllContexts();
        all.Should().HaveCount(2);
        all.Should().Contain(("*", "/", "global"));
        all.Should().Contain(("docs", "/api", "API docs"));
    }

    [Fact]
    public void FindContextForPath_LongestPrefixMatch()
    {
        var config = new CollectionConfig
        {
            GlobalContext = "fallback",
            Collections = new()
            {
                ["docs"] = new Collection
                {
                    Path = "/docs",
                    Context = new()
                    {
                        { "/", "Root context" },
                        { "/api", "API context" },
                        { "/api/v2", "API v2 context" },
                    }
                }
            }
        };
        var mgr = this.CreateInlineManager(config);

        mgr.FindContextForPath("docs", "/api/v2/endpoint.md").Should().Be("API v2 context");
        mgr.FindContextForPath("docs", "/api/v1/endpoint.md").Should().Be("API context");
        mgr.FindContextForPath("docs", "/readme.md").Should().Be("Root context");
    }

    [Fact]
    public void FindContextForPath_FallsBackToGlobal()
    {
        var config = new CollectionConfig
        {
            GlobalContext = "global fallback",
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/docs" }
            }
        };
        var mgr = this.CreateInlineManager(config);
        mgr.FindContextForPath("docs", "/any/path").Should().Be("global fallback");
    }

    [Theory]
    [InlineData("valid-name", true)]
    [InlineData("name_123", true)]
    [InlineData("UPPER", true)]
    [InlineData("has space", false)]
    [InlineData("has.dot", false)]
    [InlineData("has/slash", false)]
    [InlineData("", false)]
    public void IsValidCollectionName_Validates(string name, bool expected)
    {
        ConfigManager.IsValidCollectionName(name).Should().Be(expected);
    }

    [Fact]
    public void FileConfigSource_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".yml";
        try
        {
            var source = new FileConfigSource(tempFile);
            var config = new CollectionConfig
            {
                GlobalContext = "test context",
                Collections = new()
                {
                    ["docs"] = new Collection
                    {
                        Path = "/home/docs",
                        Pattern = "**/*.md",
                        Ignore = new List<string> { "node_modules/**" },
                        Context = new() { { "/", "Root" } },
                        IncludeByDefault = true,
                    }
                }
            };

            source.Save(config);
            var loaded = source.Load();

            loaded.GlobalContext.Should().Be("test context");
            loaded.Collections.Should().ContainKey("docs");
            loaded.Collections["docs"].Path.Should().Be("/home/docs");
            loaded.Collections["docs"].Ignore.Should().Contain("node_modules/**");
            loaded.Collections["docs"].Context.Should().ContainKey("/");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void FileConfigSource_Load_ReturnsEmpty_WhenFileMissing()
    {
        var source = new FileConfigSource("/nonexistent/path.yml");
        var config = source.Load();
        config.Should().NotBeNull();
        config.Collections.Should().BeEmpty();
    }
}
