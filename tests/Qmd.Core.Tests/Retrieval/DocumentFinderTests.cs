using FluentAssertions;
using Qmd.Core.Configuration;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Retrieval;

namespace Qmd.Core.Tests.Retrieval;

public class DocumentFinderTests : IDisposable
{
    private readonly SqliteDatabase _db;

    public DocumentFinderTests()
    {
        _db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(_db);
        SeedData();
    }

    public void Dispose() => _db.Dispose();

    private void SeedData()
    {
        // Seed collection config
        var config = new CollectionConfig
        {
            Collections = new() { ["docs"] = new Collection { Path = "/home/docs" } }
        };
        ConfigSync.SyncToDb(_db, config);

        // Seed documents
        ContentHasher.InsertContent(_db, "aabbcc112233", "# API Reference\nREST API docs.", "2025-01-01");
        ContentHasher.InsertContent(_db, "ddeeff445566", "# Guide\nGetting started.", "2025-01-01");
        DocumentOperations.InsertDocument(_db, "docs", "api.md", "API Reference", "aabbcc112233", "2025-01-01", "2025-01-01");
        DocumentOperations.InsertDocument(_db, "docs", "guide.md", "Guide", "ddeeff445566", "2025-01-01", "2025-01-01");
    }

    [Fact]
    public void FindDocument_ByVirtualPath()
    {
        var result = DocumentFinder.FindDocument(_db, "qmd://docs/api.md");
        result.IsFound.Should().BeTrue();
        result.Document!.Title.Should().Be("API Reference");
        result.Document.Filepath.Should().Be("qmd://docs/api.md");
    }

    [Fact]
    public void FindDocument_ByDocid()
    {
        var result = DocumentFinder.FindDocument(_db, "#aabbcc");
        result.IsFound.Should().BeTrue();
        result.Document!.DisplayPath.Should().Contain("api.md");
    }

    [Fact]
    public void FindDocument_ByDocidWithoutHash()
    {
        var result = DocumentFinder.FindDocument(_db, "aabbcc");
        result.IsFound.Should().BeTrue();
    }

    [Fact]
    public void FindDocument_ByRelativePath()
    {
        var result = DocumentFinder.FindDocument(_db, "guide.md");
        result.IsFound.Should().BeTrue();
        result.Document!.Title.Should().Be("Guide");
    }

    [Fact]
    public void FindDocument_ByPartialVirtualPath()
    {
        var result = DocumentFinder.FindDocument(_db, "docs/api.md");
        // Should find via LIKE %docs/api.md match
        result.IsFound.Should().BeTrue();
    }

    [Fact]
    public void FindDocument_NotFound_ReturnsSimilarFiles()
    {
        var result = DocumentFinder.FindDocument(_db, "nonexistent.md");
        result.IsFound.Should().BeFalse();
        result.NotFound.Should().NotBeNull();
        result.NotFound!.Query.Should().Be("nonexistent.md");
    }

    [Fact]
    public void FindDocument_WithBody()
    {
        var result = DocumentFinder.FindDocument(_db, "qmd://docs/api.md", includeBody: true);
        result.IsFound.Should().BeTrue();
        result.Document!.Body.Should().Contain("REST API");
    }

    [Fact]
    public void FindDocument_StripLineNumber()
    {
        var result = DocumentFinder.FindDocument(_db, "qmd://docs/api.md:10");
        result.IsFound.Should().BeTrue();
        result.Document!.Title.Should().Be("API Reference");
    }

    [Fact]
    public void GetDocumentBody_FullContent()
    {
        var body = DocumentFinder.GetDocumentBody(_db, "qmd://docs/api.md");
        body.Should().NotBeNull();
        body.Should().Contain("REST API");
    }

    [Fact]
    public void GetDocumentBody_LineSlicing()
    {
        var body = DocumentFinder.GetDocumentBody(_db, "qmd://docs/api.md", fromLine: 2, maxLines: 1);
        body.Should().NotBeNull();
        body.Should().Contain("REST API");
        body!.Split('\n').Should().HaveCount(1);
    }

    [Fact]
    public void GetDocumentBody_NotFound()
    {
        var body = DocumentFinder.GetDocumentBody(_db, "qmd://docs/nonexistent.md");
        body.Should().BeNull();
    }

    // =========================================================================
    // findDocument by display_path
    // =========================================================================

    [Fact]
    public void FindDocument_ByDisplayPath_WithoutQmdPrefix()
    {
        // Lookup using collection/path format without qmd://
        var result = DocumentFinder.FindDocument(_db, "docs/api.md");
        result.IsFound.Should().BeTrue();
    }

    // =========================================================================
    // findDocument includes context from path_contexts
    // =========================================================================

    [Fact]
    public void FindDocument_IncludesContextFromPathContexts()
    {
        // FindDocument includes context from path_contexts
        using var db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(db);

        // Seed collection with context on "docs" path
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["mycol"] = new Collection
                {
                    Path = "/path",
                    Context = new Dictionary<string, string> { ["/docs"] = "Documentation" }
                }
            }
        };
        ConfigSync.SyncToDb(db, config);

        // Insert document under docs/
        ContentHasher.InsertContent(db, "ctx112233", "# My Doc\nContent", "2025-01-01");
        DocumentOperations.InsertDocument(db, "mycol", "docs/mydoc.md", "My Doc", "ctx112233", "2025-01-01", "2025-01-01");

        var result = DocumentFinder.FindDocument(db, "/path/docs/mydoc.md");
        result.IsFound.Should().BeTrue();
        result.Document!.Context.Should().Be("Documentation");
    }

    // =========================================================================
    // findDocument includes hierarchical contexts
    // =========================================================================

    [Fact]
    public void FindDocument_IncludesHierarchicalContexts()
    {
        // FindDocument includes hierarchical contexts (global + collection + path)
        using var db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(db);

        var config = new CollectionConfig
        {
            GlobalContext = "Global context for all documents",
            Collections = new()
            {
                ["archive"] = new Collection
                {
                    Path = "/archive",
                    Context = new Dictionary<string, string>
                    {
                        ["/"] = "Archive collection context",
                        ["/podcasts"] = "Podcast episodes",
                        ["/podcasts/external"] = "External podcast interviews",
                    }
                }
            }
        };
        ConfigSync.SyncToDb(db, config);

        // Insert document in nested path
        ContentHasher.InsertContent(db, "hier112233", "# Interview\nContent of interview", "2025-01-01");
        DocumentOperations.InsertDocument(db, "archive", "podcasts/external/2024-jan-interview.md",
            "Interview", "hier112233", "2025-01-01", "2025-01-01");

        var result = DocumentFinder.FindDocument(db, "/archive/podcasts/external/2024-jan-interview.md");
        result.IsFound.Should().BeTrue();
        // Should have all contexts joined with double newlines (general to specific)
        result.Document!.Context.Should().Be(
            "Global context for all documents\n\n" +
            "Archive collection context\n\n" +
            "Podcast episodes\n\n" +
            "External podcast interviews");
    }

    // =========================================================================
    // findDocument expands ~ to home directory
    // =========================================================================

    [Fact]
    public void FindDocument_ExpandsTildeToHomeDirectory()
    {
        // FindDocument expands ~ to home directory
        using var db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(db);

        var home = Qmd.Core.Paths.QmdPaths.HomeDir();
        var config = new CollectionConfig
        {
            Collections = new() { ["home"] = new Collection { Path = home } }
        };
        ConfigSync.SyncToDb(db, config);

        ContentHasher.InsertContent(db, "tilde112233", "# My Doc\nTilde test content", "2025-01-01");
        DocumentOperations.InsertDocument(db, "home", "docs/mydoc.md", "My Doc", "tilde112233", "2025-01-01", "2025-01-01");

        // Query using ~/path — should expand tilde and find the document
        var result = DocumentFinder.FindDocument(db, "~/docs/mydoc.md");
        result.IsFound.Should().BeTrue();
    }
}
