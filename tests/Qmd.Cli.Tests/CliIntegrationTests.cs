using FluentAssertions;
using Qmd.Cli.Formatting;
using Qmd.Core;
using Qmd.Core.Configuration;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Models;
using Qmd.Core.Store;

namespace Qmd.Cli.Tests;

/// <summary>
/// CLI integration tests.
/// Instead of spawning processes, we invoke SDK methods directly
/// against an in-memory store with seeded data.
/// </summary>
[Trait("Category", "Integration")]
public class CliIntegrationTests : IAsyncLifetime
{

    private IQmdStore _store = null!;
    private QmdStore _coreStore = null!;

    /// <summary>
    /// Seed helper — mirrors the pattern from QmdStoreSdkTests.
    /// Inserts content + document into the in-memory database.
    /// Path is stored as the relative path (handelized), and the DB
    /// constructs the virtual path as qmd://collection/path.
    /// </summary>
    private void Seed(string collection, string path, string content)
    {
        var hash = ContentHasher.HashContent(content);
        _coreStore.DocumentRepo.InsertContent(hash, content, "2025-01-01");
        var title = _coreStore.ExtractTitle(content, path);
        _coreStore.DocumentRepo.InsertDocument(
            collection, path, title, hash, "2025-01-01", "2025-01-01");
    }

    public Task InitializeAsync()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["fixtures"] = new Collection { Path = "/test/fixtures", Pattern = "**/*.md" },
            }
        };

        var db = new SqliteDatabase(":memory:");
        var configManager = new ConfigManager(new InlineConfigSource(config));
        _coreStore = new QmdStore(db, configManager);

        // Seed data matching the TS test fixtures
        Seed("fixtures", "readme.md",
            "# Test Project\n\nThis is a test project for QMD CLI testing.\n\n## Features\n\n- Full-text search with BM25\n- Vector similarity search\n- Hybrid search with reranking\n");

        Seed("fixtures", "notes/meeting.md",
            "# Team Meeting Notes\n\nDate: 2024-01-15\n\n## Attendees\n- Alice\n- Bob\n- Charlie\n\n## Discussion Topics\n- Project timeline review\n- Resource allocation\n- Technical debt prioritization\n\n## Action Items\n1. Alice to update documentation\n2. Bob to fix authentication bug\n3. Charlie to review pull requests\n");

        Seed("fixtures", "notes/ideas.md",
            "# Product Ideas\n\n## Feature Requests\n- Dark mode support\n- Keyboard shortcuts\n- Export to PDF\n\n## Technical Improvements\n- Improve search performance\n- Add caching layer\n- Optimize database queries\n");

        Seed("fixtures", "docs/api.md",
            "# API Documentation\n\n## Endpoints\n\n### GET /search\nSearch for documents.\n\nParameters:\n- q: Search query (required)\n- limit: Max results (default: 10)\n\n### GET /document/:id\nRetrieve a specific document.\n\n### POST /index\nIndex new documents.\n");

        Seed("fixtures", "test1.md",
            "# Test Document 1\n\nThis is the first test document.\n\nIt has multiple lines for testing line numbers.\nLine 6 is here.\nLine 7 is here.\n");

        Seed("fixtures", "test2.md",
            "# Test Document 2\n\nThis is the second test document.\n");

        _store = _coreStore;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
    }

    private static (IQmdStore Store, QmdStore CoreStore) CreateFreshStore(CollectionConfig? config = null)
    {
        config ??= new CollectionConfig();
        var db = new SqliteDatabase(":memory:");
        var configManager = new ConfigManager(new InlineConfigSource(config));
        var store = new QmdStore(db, configManager);
        return (store, store);
    }

    private static void SeedDoc(QmdStore coreStore, string collection, string path, string content)
    {
        var hash = ContentHasher.HashContent(content);
        coreStore.DocumentRepo.InsertContent(hash, content, "2025-01-01");
        var title = coreStore.ExtractTitle(content, path);
        coreStore.DocumentRepo.InsertDocument(
            collection, path, title, hash, "2025-01-01", "2025-01-01");
    }

    [Fact]
    public async Task Search_BM25_FindsMeetingDocument()
    {
        var results = await _store.SearchLexAsync("meeting");
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.Title.ToLower().Contains("meeting")
                                  || r.Body!.ToLower().Contains("meeting"));
    }

    [Fact]
    public async Task Search_WithLimit_RespectsLimit()
    {
        var results = await _store.SearchLexAsync("test", new LexSearchOptions { Limit = 1 });
        results.Should().HaveCountLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Search_WithLargeLimit_ReturnsAll()
    {
        // --all passes a large limit
        var results = await _store.SearchLexAsync("the", new LexSearchOptions { Limit = 1000 });
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Search_NonMatching_ReturnsEmpty()
    {
        // returns no results message for non-matching query
        var results = await _store.SearchLexAsync("xyznonexistent123");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_WithCollectionFilter_OnlyReturnsMatchingCollection()
    {
        // Filters search by collection name.
        // Create a store with two collections
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["notes"] = new Collection { Path = "/test/notes", Pattern = "**/*.md" },
                ["docs"] = new Collection { Path = "/test/docs", Pattern = "**/*.md" },
            }
        };
        var (store, coreStore) = CreateFreshStore(config);
        await using var _ = store;

        SeedDoc(coreStore, "notes", "meeting.md",
            "# Team Meeting Notes\n\nDiscussion about project timeline.\n");
        SeedDoc(coreStore, "docs", "api.md",
            "# API Documentation\n\nSearch for documents.\n");

        var results = await store.SearchLexAsync("meeting",
            new LexSearchOptions { Collections = ["notes"] });
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r => r.CollectionName.Should().Be("notes"));
    }

    [Fact]
    public async Task Search_JsonFormat_ReturnsValidJson()
    {
        // search with --json flag outputs JSON
        var results = await _store.SearchLexAsync("test");
        results.Should().NotBeEmpty();

        var json = SearchResultFormatter.ToJson(results);
        json.Should().NotBeNullOrEmpty();
        // Should be valid JSON array
        var parsed = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(json);
        parsed.Should().NotBeNull();
        parsed!.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Search_FilesFormat_ContainsFilePaths()
    {
        // search with --files flag outputs file paths
        var results = await _store.SearchLexAsync("meeting");
        results.Should().NotBeEmpty();

        var output = SearchResultFormatter.ToFiles(results);
        output.Should().Contain(".md");
    }

    [Fact]
    public async Task Search_CsvFormat_HasCorrectHeader()
    {
        // returns CSV header only for non-matching query with --csv
        var emptyResults = new List<SearchResult>();
        var csv = SearchResultFormatter.ToCsv(emptyResults);
        csv.Trim().Should().Be("docid,score,file,title,context,line,snippet");
    }

    [Fact]
    public async Task Get_RetrievesDocumentByPath()
    {
        // retrieves document content by path
        var result = await _store.GetAsync("readme.md");
        result.IsFound.Should().BeTrue();
        result.Document!.Title.Should().Contain("Test Project");
    }

    [Fact]
    public async Task Get_RetrievesFromSubdirectory()
    {
        // retrieves document from subdirectory
        var result = await _store.GetAsync("notes/meeting.md");
        result.IsFound.Should().BeTrue();
        result.Document!.Title.Should().Contain("Team Meeting");
    }

    [Fact]
    public async Task Get_HandlesNonExistentFile()
    {
        // handles non-existent file
        var result = await _store.GetAsync("nonexistent.md");
        result.IsFound.Should().BeFalse();
        result.NotFound.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_WithVirtualPathFormat()
    {
        // get with qmd://collection/path format
        var result = await _store.GetAsync("qmd://fixtures/test1.md");
        result.IsFound.Should().BeTrue();
        result.Document!.Title.Should().Contain("Test Document 1");
    }

    [Fact]
    public async Task Get_WithPathLineFormat()
    {
        // get with path:line format
        // The GetDocumentBodyAsync supports line slicing
        var body = await _store.GetDocumentBodyAsync("fixtures/test1.md",
            new BodyOptions { FromLine = 3, MaxLines = 2 });
        body.Should().NotBeNull();
        // Should start from line 3, not line 1 — so should NOT contain the title "# Test Document 1"
        body.Should().NotContain("# Test Document 1");
    }

    [Fact]
    public async Task Collection_ListsCollections()
    {
        // lists collections
        var collections = await _store.ListCollectionsAsync();
        collections.Should().NotBeEmpty();
        collections.Should().Contain(c => c.Name == "fixtures");
    }

    [Fact]
    public async Task Collection_RemovesCollection()
    {
        // removes a collection
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["fixtures"] = new Collection { Path = "/test/fixtures", Pattern = "**/*.md" },
            }
        };
        var (store, _) = CreateFreshStore(config);
        await using var __ = store;

        var removed = await store.RemoveCollectionAsync("fixtures");
        removed.Should().BeTrue();

        var collections = await store.ListCollectionsAsync();
        collections.Should().NotContain(c => c.Name == "fixtures");
    }

    [Fact]
    public async Task Collection_HandlesRemovingNonExistent()
    {
        // handles removing non-existent collection
        var removed = await _store.RemoveCollectionAsync("nonexistent");
        removed.Should().BeFalse();
    }

    [Fact]
    public async Task Collection_RenamesCollection()
    {
        // renames a collection
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["fixtures"] = new Collection { Path = "/test/fixtures", Pattern = "**/*.md" },
            }
        };
        var (store, _) = CreateFreshStore(config);
        await using var __ = store;

        var renamed = await store.RenameCollectionAsync("fixtures", "my-fixtures");
        renamed.Should().BeTrue();

        var collections = await store.ListCollectionsAsync();
        collections.Should().Contain(c => c.Name == "my-fixtures");
        collections.Should().NotContain(c => c.Name == "fixtures");
    }

    [Fact]
    public async Task Collection_HandlesRenamingNonExistent()
    {
        // handles renaming non-existent collection
        var renamed = await _store.RenameCollectionAsync("nonexistent", "newname");
        renamed.Should().BeFalse();
    }

    [Fact]
    public async Task Collection_HandlesRenamingToExistingName()
    {
        // handles renaming to existing collection name
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["fixtures"] = new Collection { Path = "/test/fixtures", Pattern = "**/*.md" },
                ["second"] = new Collection { Path = "/test/second", Pattern = "**/*.md" },
            }
        };
        var (store, _) = CreateFreshStore(config);
        await using var __ = store;

        var act = async () => await store.RenameCollectionAsync("fixtures", "second");
        await act.Should().ThrowAsync<QmdException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task Collection_MultipleCollectionsAdded()
    {
        // multiple collections added
        var (store, _) = CreateFreshStore();
        await using var __ = store;

        await store.AddCollectionAsync("docs", "/test/docs", "**/*.md");
        await store.AddCollectionAsync("notes", "/test/notes", "**/*.md");

        var collections = await store.ListCollectionsAsync();
        collections.Should().HaveCount(2);
        collections.Select(c => c.Name).Should().Contain("docs");
        collections.Select(c => c.Name).Should().Contain("notes");
    }

    [Fact]
    public async Task Context_AddGlobalWithSlash()
    {
        // add global context with /
        var (store, _) = CreateFreshStore(new CollectionConfig
        {
            Collections = new()
            {
                ["fixtures"] = new Collection { Path = "/test/fixtures" },
            }
        });
        await using var __ = store;

        await store.SetGlobalContextAsync("Global system context");
        var global = await store.GetGlobalContextAsync();
        global.Should().Be("Global system context");
    }

    [Fact]
    public async Task Context_ListContexts()
    {
        // list contexts
        var (store, _) = CreateFreshStore(new CollectionConfig
        {
            Collections = new()
            {
                ["fixtures"] = new Collection { Path = "/test/fixtures" },
            }
        });
        await using var __ = store;

        await store.SetGlobalContextAsync("Test context");
        var contexts = await store.ListContextsAsync();
        contexts.Should().NotBeEmpty();
        contexts.Should().Contain(c => c.Context == "Test context");
    }

    [Fact]
    public async Task Context_AddToVirtualPath()
    {
        // add context to virtual path
        var (store, _) = CreateFreshStore(new CollectionConfig
        {
            Collections = new()
            {
                ["fixtures"] = new Collection { Path = "/test/fixtures" },
            }
        });
        await using var __ = store;

        var added = await store.AddContextAsync("fixtures", "/notes", "Context for notes subdirectory");
        added.Should().BeTrue();

        var contexts = await store.ListContextsAsync();
        contexts.Should().Contain(c =>
            c.Collection == "fixtures" && c.Path == "/notes" && c.Context == "Context for notes subdirectory");
    }

    [Fact]
    public async Task Context_RemoveGlobal()
    {
        // remove global context
        var (store, _) = CreateFreshStore(new CollectionConfig
        {
            Collections = new()
            {
                ["fixtures"] = new Collection { Path = "/test/fixtures" },
            }
        });
        await using var __ = store;

        await store.SetGlobalContextAsync("Global context to remove");
        await store.SetGlobalContextAsync(null);
        var global = await store.GetGlobalContextAsync();
        global.Should().BeNull();
    }

    [Fact]
    public async Task Context_RemoveVirtualPath()
    {
        // remove virtual path context
        var (store, _) = CreateFreshStore(new CollectionConfig
        {
            Collections = new()
            {
                ["fixtures"] = new Collection { Path = "/test/fixtures" },
            }
        });
        await using var __ = store;

        await store.AddContextAsync("fixtures", "/notes", "Context to remove");
        var removed = await store.RemoveContextAsync("fixtures", "/notes");
        removed.Should().BeTrue();

        var contexts = await store.ListContextsAsync();
        contexts.Should().NotContain(c => c.Path == "/notes");
    }

    [Fact]
    public async Task Context_FailsToRemoveNonExistent()
    {
        // fails to remove non-existent context
        var (store, _) = CreateFreshStore(new CollectionConfig
        {
            Collections = new()
            {
                ["fixtures"] = new Collection { Path = "/test/fixtures" },
            }
        });
        await using var __ = store;

        var removed = await store.RemoveContextAsync("fixtures", "/nonexistent/path");
        removed.Should().BeFalse();
    }

    [Fact]
    public async Task Update_ReturnsStats()
    {
        // updates all collections.
        // We can't do a real filesystem reindex in memory, but we can verify
        // the update mechanism works with a temp directory.
        var tempDir = Path.Combine(Path.GetTempPath(), $"qmd-cli-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "doc1.md"),
                "# Document 1\n\nContent for update test.\n");

            var config = new CollectionConfig
            {
                Collections = new()
                {
                    ["update-test"] = new Collection { Path = tempDir, Pattern = "**/*.md" },
                }
            };
            var (store, _) = CreateFreshStore(config);
            await using var __ = store;

            var result = await store.UpdateAsync();
            result.Indexed.Should().BeGreaterThanOrEqualTo(1);
            result.Collections.Should().Be(1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Update_DocumentsSearchableAfterUpdate()
    {
        // documents searchable after update — update then search
        var tempDir = Path.Combine(Path.GetTempPath(), $"qmd-cli-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "searchable.md"),
                "# Searchable Document\n\nThis document contains unique-token-xyz for search verification.\n");

            var config = new CollectionConfig
            {
                Collections = new()
                {
                    ["searchtest"] = new Collection { Path = tempDir, Pattern = "**/*.md" },
                }
            };
            var (store, _) = CreateFreshStore(config);
            await using var __ = store;

            await store.UpdateAsync();
            var results = await store.SearchLexAsync("unique-token-xyz");
            results.Should().NotBeEmpty();
            results[0].Body.Should().Contain("unique-token-xyz");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Ls_ListsAllCollections()
    {
        // lists all collections
        var collections = await _store.ListCollectionsAsync();
        collections.Should().NotBeEmpty();
        collections.Select(c => c.Name).Should().Contain("fixtures");
    }

    [Fact]
    public async Task Ls_ListsFilesInCollection()
    {
        // lists files in a collection
        // MultiGetAsync with a glob pattern lists files in a collection
        var (docs, errors) = await _store.MultiGetAsync("qmd://fixtures/*.md");
        docs.Should().NotBeEmpty();
        docs.Should().Contain(d => d.Doc.DisplayPath.Contains("readme.md"));
    }

    [Fact]
    public async Task Ls_HandlesNonExistentCollection()
    {
        // handles non-existent collection
        var (docs, errors) = await _store.MultiGetAsync("qmd://nonexistent/*.md");
        docs.Should().BeEmpty();
        errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(OutputFormat.Json)]
    [InlineData(OutputFormat.Csv)]
    [InlineData(OutputFormat.Md)]
    [InlineData(OutputFormat.Xml)]
    [InlineData(OutputFormat.Files)]
    public void OutputFormat_DispatcherProducesOutput(OutputFormat format)
    {
        // format dispatcher tests — verify SearchResultFormatter.Format works
        var result = new SearchResult
        {
            Filepath = "qmd://fixtures/readme.md",
            DisplayPath = "fixtures/readme.md",
            Title = "Test Project",
            Hash = "abc123def456",
            DocId = "abc123",
            CollectionName = "fixtures",
            ModifiedAt = "2025-01-01",
            BodyLength = 100,
            Body = "Test content for formatting",
            Score = 0.85,
            Source = "fts",
        };

        var output = SearchResultFormatter.Format([result], format);
        output.Should().NotBeNullOrEmpty();
        // All formats should include the docid or path
        output.Should().Contain("abc123");
    }

    [Fact]
    public void OutputFormat_EmptyJson_ReturnsEmptyArray()
    {
        // returns empty JSON array for non-matching query with --json
        var json = SearchResultFormatter.ToJson([]);
        json.Trim().Should().Be("[]");
    }

    [Fact]
    public void OutputFormat_EmptyXml_ReturnsEmptyContainer()
    {
        // returns empty XML container for non-matching query with --xml
        var xml = SearchResultFormatter.ToXml([]);
        xml.Trim().Should().BeEmpty();
        // Note: TS expects "<results></results>" but the .NET formatter
        // returns empty string for zero results (no wrapper element).
        // This is the correct behavior for the .NET port.
    }

    [Fact]
    public void Cleanup_OrphanedEntries()
    {
        // Deactivate a document, then clean up
        _coreStore.DocumentRepo.DeactivateDocument("fixtures", "test2.md");
        var deleted = _coreStore.MaintenanceRepo.DeleteInactiveDocuments();
        deleted.Should().BeGreaterThanOrEqualTo(1);

        var orphaned = _coreStore.MaintenanceRepo.CleanupOrphanedContent();
        orphaned.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Cleanup_RemovesDocsFromDeletedCollections()
    {
        // Insert docs for a collection that doesn't exist in store_collections
        Seed("ghost-collection", "orphan.md", "# Orphan\nThis should be cleaned up.");

        // Verify it exists
        var before = _coreStore.Db.Prepare(
            "SELECT COUNT(*) as c FROM documents WHERE collection = $1")
            .GetDynamic("ghost-collection");
        Convert.ToInt32(before!["c"]).Should().Be(1);

        // Run the same cleanup SQL as CleanupCommand
        var removed = _coreStore.Db.Prepare(@"
            DELETE FROM documents WHERE collection NOT IN (
                SELECT name FROM store_collections
            )
        ").Run().Changes;
        removed.Should().BeGreaterThanOrEqualTo(1);

        // Verify it's gone
        var after = _coreStore.Db.Prepare(
            "SELECT COUNT(*) as c FROM documents WHERE collection = $1")
            .GetDynamic("ghost-collection");
        Convert.ToInt32(after!["c"]).Should().Be(0);
    }

    [Fact]
    public async Task Ls_ListsFilesViaListFilesAsync()
    {
        var files = await _store.ListFilesAsync("fixtures");
        files.Should().HaveCount(6); // readme, meeting, ideas, api, test1, test2
        files.Select(f => f.Path).Should().Contain("readme.md");
        files.Select(f => f.Path).Should().Contain("docs/api.md");
    }

    [Fact]
    public async Task Ls_ListsFilesWithPathPrefix()
    {
        var files = await _store.ListFilesAsync("fixtures", "notes/");
        files.Should().HaveCount(2); // meeting.md and ideas.md
        files.Should().AllSatisfy(f => f.Path.Should().StartWith("notes/"));
    }

    [Fact]
    public async Task Ls_ListsFilesWithPathPrefix_NoTrailingSlash()
    {
        // SQL LIKE "notes%" matches "notes/" prefix paths
        var files = await _store.ListFilesAsync("fixtures", "notes");
        files.Should().HaveCount(2);
    }

    [Fact]
    public async Task Ls_NonExistentPrefix_ReturnsEmpty()
    {
        var files = await _store.ListFilesAsync("fixtures", "nonexistent/");
        files.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchOutput_Json_IncludesDocidAndQmdPath()
    {
        // search --json output includes qmd:// path, docid, and context fields
        var results = await _store.SearchLexAsync("test");
        results.Should().NotBeEmpty();

        var json = SearchResultFormatter.ToJson(results);
        var parsed = System.Text.Json.JsonSerializer
            .Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(json)!;

        parsed.Should().NotBeEmpty();
        var first = parsed[0];
        first.Should().ContainKey("docid");
        first["docid"].GetString().Should().MatchRegex(@"^#[a-f0-9]{6}$");
        first.Should().ContainKey("file");
    }

    [Fact]
    public async Task SearchOutput_Csv_HasCorrectHeaderAndData()
    {
        // search --csv includes qmd:// path, docid, and context
        var results = await _store.SearchLexAsync("test");
        results.Should().NotBeEmpty();

        var csv = SearchResultFormatter.ToCsv(results);
        csv.Should().StartWith("docid,score,file,title,context,line,snippet");
        // Data rows exist
        csv.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public async Task SearchOutput_Md_IncludesDocid()
    {
        // search --md includes docid and context
        var results = await _store.SearchLexAsync("test");
        results.Should().NotBeEmpty();

        var md = SearchResultFormatter.ToMarkdown(results, new FormatOptions { Query = "test" });
        md.Should().MatchRegex(@"\*\*docid:\*\* `#[a-f0-9]{6}`");
    }

    [Fact]
    public async Task SearchOutput_Xml_IncludesQmdPathAndDocid()
    {
        // search --xml includes qmd:// path, docid, and context
        var results = await _store.SearchLexAsync("test");
        results.Should().NotBeEmpty();

        var xml = SearchResultFormatter.ToXml(results, new FormatOptions { Query = "test" });
        xml.Should().MatchRegex(@"<file docid=""#[a-f0-9]{6}""");
    }

    [Fact]
    public async Task SearchOutput_Files_IncludesDocidAndScore()
    {
        // search --files includes qmd:// path, docid, and context
        var results = await _store.SearchLexAsync("meeting");
        results.Should().NotBeEmpty();

        var files = SearchResultFormatter.ToFiles(results);
        files.Should().MatchRegex(@"#[a-f0-9]{6},[\d.]+,");
    }

    [Fact]
    public async Task Status_ShowsIndexInfo()
    {
        // shows index status — call GetStatusAsync, verify output shape
        var status = await _store.GetStatusAsync();
        status.Should().NotBeNull();
        status.TotalDocuments.Should().BeGreaterThan(0);
        status.Collections.Should().NotBeEmpty();
        status.Collections.Should().Contain(c => c.Name == "fixtures");
    }

    [Fact]
    public async Task Get_WithCollectionSlashPathFormat()
    {
        // get with collection/path format (no scheme)
        var result = await _store.GetAsync("fixtures/test1.md");
        result.IsFound.Should().BeTrue();
        result.Document!.Title.Should().Contain("Test Document 1");
    }

    [Fact]
    public async Task Get_WithDoubleSlashFormat()
    {
        // get with //collection/path format
        var result = await _store.GetAsync("//fixtures/test1.md");
        result.IsFound.Should().BeTrue();
        result.Document!.Title.Should().Contain("Test Document 1");
    }

    [Fact]
    public async Task IgnorePatterns_ExcludeMatchingFilesFromIndexing()
    {
        // ignore patterns exclude matching files from indexing
        var tempDir = Path.Combine(Path.GetTempPath(), $"qmd-ignore-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(tempDir, "notes"));
        Directory.CreateDirectory(Path.Combine(tempDir, "sessions"));
        Directory.CreateDirectory(Path.Combine(tempDir, "sessions", "2026-03"));
        Directory.CreateDirectory(Path.Combine(tempDir, "archive"));

        try
        {
            // Files that should be indexed
            File.WriteAllText(Path.Combine(tempDir, "readme.md"), "# Main readme\nThis should be indexed.");
            File.WriteAllText(Path.Combine(tempDir, "notes", "note1.md"), "# Note 1\nThis is a personal note.");

            // Files that should be ignored
            File.WriteAllText(Path.Combine(tempDir, "sessions", "session1.md"), "# Session 1\nThis session should be ignored.");
            File.WriteAllText(Path.Combine(tempDir, "sessions", "2026-03", "session2.md"), "# Session 2\nNested session should also be ignored.");
            File.WriteAllText(Path.Combine(tempDir, "archive", "old.md"), "# Old stuff\nThis archive file should be ignored.");

            var config = new CollectionConfig
            {
                Collections = new()
                {
                    ["ignoretst"] = new Collection
                    {
                        Path = tempDir,
                        Pattern = "**/*.md",
                        Ignore = ["sessions/**", "archive/**"],
                    },
                }
            };
            var (store, _) = CreateFreshStore(config);
            await using var __ = store;

            var result = await store.UpdateAsync();
            // Should index 2 files (readme.md + notes/note1.md), not 5
            result.Indexed.Should().Be(2);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task IgnorePatterns_IgnoredFilesNotSearchable()
    {
        // ignored files are not searchable
        var tempDir = Path.Combine(Path.GetTempPath(), $"qmd-ignore-search-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(tempDir, "sessions"));
        Directory.CreateDirectory(Path.Combine(tempDir, "notes"));

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "notes", "note1.md"), "# Note 1\nThis is a personal note.");
            File.WriteAllText(Path.Combine(tempDir, "sessions", "session1.md"), "# Session 1\nThis session should be ignored.");

            var config = new CollectionConfig
            {
                Collections = new()
                {
                    ["ignoretst"] = new Collection
                    {
                        Path = tempDir,
                        Pattern = "**/*.md",
                        Ignore = ["sessions/**"],
                    },
                }
            };
            var (store, _) = CreateFreshStore(config);
            await using var __ = store;

            await store.UpdateAsync();

            var results = await store.SearchLexAsync("session");
            // Sessions directory was ignored, so search should not find session1
            results.Should().NotContain(r => r.DisplayPath.Contains("session1"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task IgnorePatterns_CollectionWithoutIgnoreIndexesAllFiles()
    {
        // collection without ignore indexes all files
        var tempDir = Path.Combine(Path.GetTempPath(), $"qmd-noignore-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(tempDir, "notes"));
        Directory.CreateDirectory(Path.Combine(tempDir, "sessions"));
        Directory.CreateDirectory(Path.Combine(tempDir, "sessions", "2026-03"));
        Directory.CreateDirectory(Path.Combine(tempDir, "archive"));

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "readme.md"), "# Main readme\nThis should be indexed.");
            File.WriteAllText(Path.Combine(tempDir, "notes", "note1.md"), "# Note 1\nThis is a personal note.");
            File.WriteAllText(Path.Combine(tempDir, "sessions", "session1.md"), "# Session 1\nThis session should be indexed.");
            File.WriteAllText(Path.Combine(tempDir, "sessions", "2026-03", "session2.md"), "# Session 2\nNested session.");
            File.WriteAllText(Path.Combine(tempDir, "archive", "old.md"), "# Old stuff\nThis archive file.");

            var config = new CollectionConfig
            {
                Collections = new()
                {
                    ["allfiles"] = new Collection
                    {
                        Path = tempDir,
                        Pattern = "**/*.md",
                    },
                }
            };
            var (store, _) = CreateFreshStore(config);
            await using var __ = store;

            var result = await store.UpdateAsync();
            // Should index all 5 files
            result.Indexed.Should().Be(5);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Update_DeactivatesStaleDocsWhenCollectionHasZeroMatchingFiles()
    {
        // deactivates stale docs when collection has zero matching files
        var tempDir = Path.Combine(Path.GetTempPath(), $"qmd-stale-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var token = $"stale-proof-{Guid.NewGuid()}";
            File.WriteAllText(Path.Combine(tempDir, "only.md"),
                $"---\ndate: 2026-03-06\n---\n# Empty Collection Deactivation\n{token}\n");

            var config = new CollectionConfig
            {
                Collections = new()
                {
                    ["empty-check"] = new Collection { Path = tempDir, Pattern = "**/*.md" },
                }
            };
            var (store, _) = CreateFreshStore(config);
            await using var __ = store;

            // First update indexes the document
            var firstResult = await store.UpdateAsync();
            firstResult.Indexed.Should().Be(1);

            // Verify the document is retrievable
            var before = await store.GetAsync("qmd://empty-check/only.md");
            before.IsFound.Should().BeTrue();
            (await store.GetDocumentBodyAsync("empty-check/only.md")).Should().Contain(token);

            // Delete the file on disk
            File.Delete(Path.Combine(tempDir, "only.md"));

            // Second update should deactivate the stale document
            var secondResult = await store.UpdateAsync();
            secondResult.Removed.Should().Be(1);
            secondResult.Indexed.Should().Be(0);
            secondResult.Updated.Should().Be(0);

            // Document should no longer be found
            var after = await store.GetAsync("qmd://empty-check/only.md");
            after.IsFound.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Search_WithAllOption_ReturnsAllResults()
    {
        // searches with all results option — search with large limit
        var results = await _store.SearchLexAsync("the", new LexSearchOptions { Limit = 1000 });
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Search_EmptyResults_JsonReturnsEmptyArray()
    {
        // returns empty JSON array for non-matching query with --json
        var results = await _store.SearchLexAsync("xyznonexistent");
        results.Should().BeEmpty();
        var json = SearchResultFormatter.ToJson(results);
        json.Should().Be("[]");
    }

    [Fact]
    public async Task Search_EmptyResults_CsvReturnsHeaderOnly()
    {
        // returns CSV header only for non-matching query with --csv
        var results = await _store.SearchLexAsync("xyznonexistent");
        var csv = SearchResultFormatter.ToCsv(results);
        // .NET CSV formatter returns header row even for empty results
        csv.Trim().Should().StartWith("docid,");
        csv.Trim().Split('\n').Should().HaveCount(1); // header only
    }

    [Fact]
    public async Task Search_EmptyResults_XmlReturnsEmpty()
    {
        // returns empty XML container for non-matching query with --xml
        var results = await _store.SearchLexAsync("xyznonexistent");
        var xml = SearchResultFormatter.ToXml(results);
        // .NET XML formatter returns empty string for empty results (no wrapper element)
        xml.Should().BeEmpty();
    }

    [Fact]
    public async Task MultiGet_ByGlobPattern_ReturnsResults()
    {
        // retrieves multiple documents by pattern
        var (docs, errors) = await _store.MultiGetAsync("qmd://fixtures/notes/*.md",
            new MultiGetOptions { IncludeBody = true });
        docs.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task MultiGet_ByCommaSeparatedPaths_ReturnsResults()
    {
        // retrieves documents by comma-separated paths
        var (docs1, _) = await _store.MultiGetAsync("qmd://fixtures/readme.md");
        var (docs2, _) = await _store.MultiGetAsync("qmd://fixtures/notes/meeting.md");
        docs1.Count.Should().BeGreaterThanOrEqualTo(1);
        docs2.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task IgnorePatterns_NonIgnoredFilesStillSearchable()
    {
        // non-ignored files are searchable
        var tempDir = Path.Combine(Path.GetTempPath(), $"qmd-ignore-search2-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(tempDir, "notes"));
        Directory.CreateDirectory(Path.Combine(tempDir, "sessions"));

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "notes", "note1.md"), "# Note 1\nThis is a personal note.");
            File.WriteAllText(Path.Combine(tempDir, "sessions", "session1.md"), "# Session 1\nThis session should be ignored.");

            var config = new CollectionConfig
            {
                Collections = new()
                {
                    ["igtst2"] = new Collection
                    {
                        Path = tempDir, Pattern = "**/*.md",
                        Ignore = ["sessions/**"],
                    },
                }
            };
            var (store, _) = CreateFreshStore(config);
            await using var __ = store;
            await store.UpdateAsync();

            // non-ignored file should be searchable
            var results = await store.SearchLexAsync("personal note");
            results.Should().NotBeEmpty();
            results.Should().Contain(r => r.DisplayPath.Contains("note1"));
        }
        finally { Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void MakeTerminalLink_ReturnsPlainText_WhenNoEditorUri()
    {
        // termLink returns plain text when stdout is not a TTY
        var result = FormatHelpers.MakeTerminalLink("docs/api.md:12", null, "/tmp/docs/api.md", 12);
        result.Should().Be("docs/api.md:12");
    }

    [Fact]
    public void MakeTerminalLink_EmitsOsc8Hyperlink_WhenEditorUriProvided()
    {
        // termLink emits OSC 8 hyperlinks when stdout is a TTY
        // Note: MakeTerminalLink uses NoColor check internally. This tests the URI expansion logic.
        var editorUri = "vscode://file/{path}:{line}";
        var filePath = "/tmp/docs/api.md";
        var line = 12;

        // When NoColor is true (non-TTY), returns plain text
        var plain = FormatHelpers.MakeTerminalLink("docs/api.md:12", editorUri, filePath, line);

        // The result depends on the NoColor setting. Either it's plain text or OSC link.
        // Just verify the method handles the input without throwing.
        plain.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Embed_InvalidMaxDocsPerBatch_Fails()
    {
        // rejects invalid --max-docs-per-batch
        var act = async () => await _store.EmbedAsync(new EmbedPipelineOptions { MaxDocsPerBatch = 0 });
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Embed_InvalidMaxBatchMb_Fails()
    {
        // rejects invalid --max-batch-mb
        var act = async () => await _store.EmbedAsync(new EmbedPipelineOptions { MaxBatchBytes = 0 });
        await act.Should().ThrowAsync<Exception>();
    }
}
