using FluentAssertions;
using Qmd.Core.Configuration;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Retrieval;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Retrieval;

[Trait("Category", "Database")]
public class ContextResolverTests : IDisposable
{
    private readonly IQmdDatabase _db;

    public ContextResolverTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
    }

    public void Dispose() => _db.Dispose();

    private void SeedCollection(string name, string path, Dictionary<string, string>? context = null)
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                [name] = new Collection
                {
                    Path = path,
                    Context = context,
                }
            }
        };
        // Clear config hash to force re-sync
        _db.Prepare("DELETE FROM store_config WHERE key = $1").Run("config_hash");
        ConfigSync.SyncToDb(_db, config);
    }

    private void SeedCollectionWithGlobalContext(string name, string path,
        Dictionary<string, string>? context = null, string? globalContext = null)
    {
        var config = new CollectionConfig
        {
            GlobalContext = globalContext,
            Collections = new()
            {
                [name] = new Collection
                {
                    Path = path,
                    Context = context,
                }
            }
        };
        _db.Prepare("DELETE FROM store_config WHERE key = $1").Run("config_hash");
        ConfigSync.SyncToDb(_db, config);
    }

    private void InsertDoc(string collection, string displayPath, string title = "Test")
    {
        var body = $"# {title}\n\nTest content for {displayPath}.";
        var hash = ContentHasher.HashContent(body);
        ContentHasher.InsertContent(_db, hash, body, "2025-01-01");
        DocumentOperations.InsertDocument(_db, collection, displayPath, title, hash, "2025-01-01", "2025-01-01");
    }

    [Fact]
    public void GetContextForFile_ReturnsNull_WhenNoContextSet()
    {
        // No collections, no context — any file path should return null
        var context = ContextResolver.GetContextForFile(_db, "/some/random/path.md");
        context.Should().BeNull();
    }

    [Fact]
    public void GetContextForFile_ReturnsMatchingContext()
    {
        SeedCollection("collection", "/test/collection",
            context: new Dictionary<string, string> { ["/docs"] = "Documentation files" });

        InsertDoc("collection", "docs/readme.md", "readme");

        var context = ContextResolver.GetContextForFile(_db, "/test/collection/docs/readme.md");
        context.Should().Be("Documentation files");
    }

    [Fact]
    public void GetContextForFile_ReturnsAllMatchingContexts()
    {
        SeedCollectionWithGlobalContext("collection", "/test/collection",
            context: new Dictionary<string, string>
            {
                ["/"] = "General test files",
                ["/docs"] = "Documentation files",
                ["/docs/api"] = "API documentation",
            },
            globalContext: null);

        InsertDoc("collection", "readme.md", "readme");
        InsertDoc("collection", "docs/guide.md", "guide");
        InsertDoc("collection", "docs/api/reference.md", "reference");

        // readme.md is at root — only root context matches
        ContextResolver.GetContextForFile(_db, "/test/collection/readme.md")
            .Should().Be("General test files");

        // docs/guide.md matches root + /docs
        ContextResolver.GetContextForFile(_db, "/test/collection/docs/guide.md")
            .Should().Be("General test files\n\nDocumentation files");

        // docs/api/reference.md matches root + /docs + /docs/api
        ContextResolver.GetContextForFile(_db, "/test/collection/docs/api/reference.md")
            .Should().Be("General test files\n\nDocumentation files\n\nAPI documentation");
    }
}
