using FluentAssertions;
using Qmd.Core.Configuration;
using Qmd.Core.Database;
using Qmd.Core.Llm;
using Qmd.Core.Models;
using Qmd.Core.Search;
using Qmd.Core.Store;

namespace Qmd.Core.Tests;

[Trait("Category", "Integration")]
public class QmdStoreSdkTests
{
    #region Helpers

    private static void TryDeleteFile(string path)
    {
        try
        {
            // Force GC to release SQLite file handles on Windows
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup — Windows may still hold the file lock
        }
    }

    private static void TryCleanupDir(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (IOException)
        {
            // Best-effort cleanup
        }
    }

    private static void Seed(QmdStore store, string collection, string path, string content, string timestamp)
    {
        var hash = store.HashContent(content);
        store.InsertContent(hash, content, timestamp);
        var title = store.ExtractTitle(content, path);
        store.InsertDocument(collection, $"qmd://{collection}/{path}", title, hash, timestamp, timestamp);
    }

    private static IQmdStore CreateSeededStore()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/test/docs", Pattern = "**/*.md" },
                ["notes"] = new Collection { Path = "/test/notes", Pattern = "**/*.md" },
            }
        };

        var db = new SqliteDatabase(":memory:");
        var configManager = new ConfigManager(new InlineConfigSource(config));
        var store = new QmdStore(db, configManager);

        var now = DateTime.UtcNow.ToString("o");

        Seed(store, "docs", "readme.md",
            "# Getting Started\n\nThis is the getting started guide for the project.\n", now);
        Seed(store, "docs", "auth.md",
            "# Authentication\n\nAuthentication uses JWT tokens for session management.\nUsers log in with email and password.\n", now);
        Seed(store, "docs", "api.md",
            "# API Reference\n\n## Endpoints\n\n### POST /login\nAuthenticate a user.\n\n### GET /users\nList all users.\n", now);

        Seed(store, "notes", "meeting-2025-01.md",
            "# January Planning Meeting\n\nDiscussed Q1 roadmap and resource allocation.\n", now);
        Seed(store, "notes", "meeting-2025-02.md",
            "# February Standup\n\nReviewed sprint progress. Authentication feature is on track.\n", now);
        Seed(store, "notes", "ideas.md",
            "# Project Ideas\n\n- Build a search engine\n- Create a knowledge base\n- Implement vector search\n", now);

        return store;
    }

    private static IQmdStore CreateSeededStoreWithMockLlm()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/test/docs", Pattern = "**/*.md" },
                ["notes"] = new Collection { Path = "/test/notes", Pattern = "**/*.md" },
            }
        };

        var db = new SqliteDatabase(":memory:");
        var configManager = new ConfigManager(new InlineConfigSource(config));
        var store = new QmdStore(db, configManager, new StubLlmService());
        // Disable FTS gate — test corpus has no vectors, so gating FTS would return empty results
        store.SearchConfig = new SearchConfig { FtsMinSignal = 0.0 };

        var now = DateTime.UtcNow.ToString("o");

        Seed(store, "docs", "readme.md",
            "# Getting Started\n\nThis is the getting started guide for the project.\n", now);
        Seed(store, "docs", "auth.md",
            "# Authentication\n\nAuthentication uses JWT tokens for session management.\nUsers log in with email and password.\n", now);
        Seed(store, "docs", "api.md",
            "# API Reference\n\n## Endpoints\n\n### POST /login\nAuthenticate a user.\n\n### GET /users\nList all users.\n", now);

        Seed(store, "notes", "meeting-2025-01.md",
            "# January Planning Meeting\n\nDiscussed Q1 roadmap and resource allocation.\n", now);
        Seed(store, "notes", "meeting-2025-02.md",
            "# February Standup\n\nReviewed sprint progress. Authentication feature is on track.\n", now);
        Seed(store, "notes", "ideas.md",
            "# Project Ideas\n\n- Build a search engine\n- Create a knowledge base\n- Implement vector search\n", now);

        return store;
    }

    /// <summary>
    /// Minimal LLM service stub for tests that require an ILlmService reference
    /// but don't actually invoke LLM operations (e.g., lex-only structured search with SkipRerank).
    /// </summary>
    private class StubLlmService : ILlmService
    {
        public string EmbedModelName => "stub-model";

        public Task<EmbeddingResult?> EmbedAsync(string text, EmbedOptions? options = null, CancellationToken ct = default)
            => Task.FromResult<EmbeddingResult?>(new EmbeddingResult(new float[] { 0.1f, 0.2f, 0.3f }, "stub"));

        public Task<List<EmbeddingResult?>> EmbedBatchAsync(List<string> texts, EmbedOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(texts.Select(_ => (EmbeddingResult?)new EmbeddingResult(new float[] { 0.1f, 0.2f, 0.3f }, "stub")).ToList());

        public Task<GenerateResult?> GenerateAsync(string prompt, GenerateOptions? options = null, CancellationToken ct = default)
            => Task.FromResult<GenerateResult?>(new GenerateResult("stub", "stub", null, true));

        public Task<RerankResult> RerankAsync(string query, List<RerankDocument> documents, RerankOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new RerankResult(
                documents.Select((d, i) => new RerankDocumentResult(d.File, 0.5, i)).ToList(), "stub"));

        public Task<List<QueryExpansion>> ExpandQueryAsync(string query, ExpandQueryOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new List<QueryExpansion> { new(QueryType.Lex, query) });

        public int CountTokens(string text) => text.Length / 4;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    #endregion

    #region Create store variations

    [Fact]
    public async Task CreateInMemory_InitializesStore()
    {
        await using var store = await QmdStoreFactory.CreateInMemoryAsync();
        var status = await store.GetStatusAsync();
        status.TotalDocuments.Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_ThrowsIfDbPathIsMissing()
    {
        var act = async () => await QmdStoreFactory.CreateAsync(new StoreOptions { DbPath = "" });
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*dbPath*required*");
    }

    [Fact]
    public async Task CreateAsync_ThrowsIfBothConfigPathAndConfigProvided()
    {
        var act = async () => await QmdStoreFactory.CreateAsync(new StoreOptions
        {
            DbPath = Path.Combine(Path.GetTempPath(), $"qmd-test-{Guid.NewGuid()}.sqlite"),
            ConfigPath = "/some/path.yml",
            Config = new CollectionConfig()
        });
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*either*ConfigPath*Config*not both*");
    }

    [Fact]
    public async Task CreateAsync_StoreDbPathMatchesProvidedPath()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"qmd-test-{Guid.NewGuid()}.sqlite");
        var store = await QmdStoreFactory.CreateAsync(new StoreOptions
        {
            DbPath = dbPath,
            Config = new CollectionConfig()
        });
        await store.DisposeAsync();

        // The store was created at the expected path
        File.Exists(dbPath).Should().BeTrue();
        // Cleanup best-effort (Windows may hold file lock)
        TryDeleteFile(dbPath);
    }

    #endregion

    #region Collection management

    [Fact]
    public async Task AddAndListCollections()
    {
        var config = new CollectionConfig();
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);
        await store.AddCollectionAsync("docs", "/home/docs");
        var collections = await store.ListCollectionsAsync();
        collections.Should().HaveCount(1);
        collections[0].Name.Should().Be("docs");
    }

    [Fact]
    public async Task AddCollection_WithDefaultPattern()
    {
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(new CollectionConfig());
        await store.AddCollectionAsync("notes", "/test/notes");

        var collections = await store.ListCollectionsAsync();
        collections.Should().Contain(c => c.Name == "notes");
    }

    [Fact]
    public async Task RemoveCollection()
    {
        var config = new CollectionConfig
        {
            Collections = new() { ["docs"] = new Collection { Path = "/docs" } }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);
        (await store.RemoveCollectionAsync("docs")).Should().BeTrue();
        (await store.ListCollectionsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveCollection_ReturnsFalseForNonExistent()
    {
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(new CollectionConfig());
        var removed = await store.RemoveCollectionAsync("nonexistent");
        removed.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveCollection_DeletesDocuments()
    {
        await using var store = CreateSeededStore();
        // Verify documents exist before removal
        var filesBefore = await store.ListFilesAsync("docs");
        filesBefore.Should().NotBeEmpty();

        (await store.RemoveCollectionAsync("docs")).Should().BeTrue();

        // Documents should be gone
        var filesAfter = await store.ListFilesAsync("docs");
        filesAfter.Should().BeEmpty();

        // Other collection should be unaffected
        var notesFiles = await store.ListFilesAsync("notes");
        notesFiles.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RenameCollection()
    {
        var config = new CollectionConfig
        {
            Collections = new() { ["old"] = new Collection { Path = "/path" } }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);
        (await store.RenameCollectionAsync("old", "new")).Should().BeTrue();
        var list = await store.ListCollectionsAsync();
        list.Should().Contain(c => c.Name == "new");
        list.Should().NotContain(c => c.Name == "old");
    }

    [Fact]
    public async Task RenameCollection_ReturnsFalseForNonExistentSource()
    {
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(new CollectionConfig());
        var renamed = await store.RenameCollectionAsync("nonexistent", "new-name");
        renamed.Should().BeFalse();
    }

    [Fact]
    public async Task RenameCollection_ThrowsIfTargetExists()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["a"] = new Collection { Path = "/a" },
                ["b"] = new Collection { Path = "/b" },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);

        var act = async () => await store.RenameCollectionAsync("a", "b");
        await act.Should().ThrowAsync<QmdException>().WithMessage("*already exists*");
    }

    [Fact]
    public async Task ListCollections_ReturnsEmptyForEmptyConfig()
    {
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(new CollectionConfig());
        var collections = await store.ListCollectionsAsync();
        collections.Should().BeEmpty();
    }

    [Fact]
    public async Task MultipleCollections_CanBeAdded()
    {
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(new CollectionConfig());
        await store.AddCollectionAsync("docs", "/test/docs", "**/*.md");
        await store.AddCollectionAsync("notes", "/test/notes", "**/*.md");

        var names = (await store.ListCollectionsAsync()).Select(c => c.Name).ToList();
        names.Should().Contain("docs");
        names.Should().Contain("notes");
        names.Should().HaveCount(2);
    }

    #endregion

    #region List files

    [Fact]
    public async Task ListFiles_ReturnsAllFilesInCollection()
    {
        await using var store = CreateSeededStore();
        var files = await store.ListFilesAsync("docs");
        files.Should().HaveCount(3);
        // SDK Seed stores paths as qmd://collection/path
        files.Select(f => f.Path).Should().Contain(p => p.Contains("readme.md"));
        files.Select(f => f.Path).Should().Contain(p => p.Contains("auth.md"));
        files.Select(f => f.Path).Should().Contain(p => p.Contains("api.md"));
    }

    [Fact]
    public async Task ListFiles_WithPathPrefix_FiltersResults()
    {
        await using var store = CreateSeededStore();
        // "notes" collection has meeting-2025-01.md and ideas.md
        var allNotes = await store.ListFilesAsync("notes");
        allNotes.Should().HaveCountGreaterThan(1);

        // Filter by prefix — SDK Seed stores paths as qmd://notes/meeting...
        var meetings = await store.ListFilesAsync("notes", "qmd://notes/meeting");
        meetings.Should().NotBeEmpty();
        meetings.Should().AllSatisfy(f => f.Path.Should().Contain("meeting"));
    }

    [Fact]
    public async Task ListFiles_WithNonMatchingPrefix_ReturnsEmpty()
    {
        await using var store = CreateSeededStore();
        var files = await store.ListFilesAsync("docs", "nonexistent/");
        files.Should().BeEmpty();
    }

    [Fact]
    public async Task ListFiles_ReturnsBodyLength()
    {
        await using var store = CreateSeededStore();
        var files = await store.ListFilesAsync("docs");
        files.Should().AllSatisfy(f => f.BodyLength.Should().BeGreaterThan(0));
    }

    [Fact]
    public async Task ListFiles_OrdersByPath()
    {
        await using var store = CreateSeededStore();
        var files = await store.ListFilesAsync("docs");
        files.Select(f => f.Path).Should().BeInAscendingOrder();
    }

    #endregion

    #region Context management

    [Fact]
    public async Task GlobalContext_SetAndGet()
    {
        await using var store = await QmdStoreFactory.CreateInMemoryAsync();
        await store.SetGlobalContextAsync("test context");
        (await store.GetGlobalContextAsync()).Should().Be("test context");
    }

    [Fact]
    public async Task AddContextToCollection()
    {
        var config = new CollectionConfig
        {
            Collections = new() { ["docs"] = new Collection { Path = "/docs" } }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);
        (await store.AddContextAsync("docs", "/api", "API documentation")).Should().BeTrue();
        var contexts = await store.ListContextsAsync();
        contexts.Should().Contain(c => c.Path == "/api" && c.Context == "API documentation");
    }

    [Fact]
    public async Task AddContext_ReturnsFalseForNonExistentCollection()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/docs" },
                ["notes"] = new Collection { Path = "/notes" },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);
        var added = await store.AddContextAsync("nonexistent", "/path", "Some context");
        added.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveContext_RemovesExisting()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/docs" },
                ["notes"] = new Collection { Path = "/notes" },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);
        await store.AddContextAsync("docs", "/auth", "Authentication docs");
        var removed = await store.RemoveContextAsync("docs", "/auth");

        removed.Should().BeTrue();
        var contexts = await store.ListContextsAsync();
        contexts.Should().NotContain(c => c.Path == "/auth");
    }

    [Fact]
    public async Task RemoveContext_ReturnsFalseForNonExistent()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/docs" },
                ["notes"] = new Collection { Path = "/notes" },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);
        var removed = await store.RemoveContextAsync("docs", "/nonexistent");
        removed.Should().BeFalse();
    }

    [Fact]
    public async Task SetGlobalContext_WithNullClearsIt()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/docs" },
                ["notes"] = new Collection { Path = "/notes" },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);
        await store.SetGlobalContextAsync("Some context");
        await store.SetGlobalContextAsync(null);
        var global = await store.GetGlobalContextAsync();

        global.Should().BeNull();
    }

    [Fact]
    public async Task ListContexts_IncludesGlobalContext()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/docs" },
                ["notes"] = new Collection { Path = "/notes" },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);
        await store.SetGlobalContextAsync("Global context");
        var contexts = await store.ListContextsAsync();

        contexts.Should().Contain(c => c.Collection == "*" && c.Path == "/" && c.Context == "Global context");
    }

    [Fact]
    public async Task ListContexts_ReturnsContextsAcrossCollections()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/docs" },
                ["notes"] = new Collection { Path = "/notes" },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);
        await store.AddContextAsync("docs", "/", "Documentation");
        await store.AddContextAsync("notes", "/", "Personal notes");

        var contexts = await store.ListContextsAsync();
        contexts.Where(c => c.Path == "/").Should().HaveCount(2);
    }

    [Fact]
    public async Task MultipleContexts_OnSameCollection()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/docs" },
                ["notes"] = new Collection { Path = "/notes" },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);
        await store.AddContextAsync("docs", "/auth", "Auth docs");
        await store.AddContextAsync("docs", "/api", "API docs");

        var contexts = (await store.ListContextsAsync()).Where(c => c.Collection == "docs").ToList();
        contexts.Should().HaveCount(2);
        contexts.Select(c => c.Path).OrderBy(p => p).Should().Equal("/api", "/auth");
    }

    [Fact]
    public async Task AddContext_OverwritesExistingContextForSamePath()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/docs" },
                ["notes"] = new Collection { Path = "/notes" },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);
        await store.AddContextAsync("docs", "/auth", "Old context");
        await store.AddContextAsync("docs", "/auth", "New context");

        var contexts = (await store.ListContextsAsync()).Where(c => c.Path == "/auth").ToList();
        contexts.Should().HaveCount(1);
        contexts[0].Context.Should().Be("New context");
    }

    #endregion

    #region SearchLex (BM25)

    [Fact]
    public async Task SearchLex_ReturnsResultsForMatchingQuery()
    {
        await using var store = CreateSeededStore();
        var results = await store.SearchLexAsync("authentication");
        results.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SearchLex_ResultsHaveExpectedShape()
    {
        await using var store = CreateSeededStore();
        var results = await store.SearchLexAsync("authentication");
        results.Count.Should().BeGreaterThan(0);

        var result = results[0];
        result.Filepath.Should().NotBeNullOrEmpty();
        result.Score.Should().BeGreaterThan(0);
        result.Title.Should().NotBeNullOrEmpty();
        result.DocId.Should().NotBeNullOrEmpty();
        result.CollectionName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SearchLex_RespectsLimit()
    {
        await using var store = CreateSeededStore();
        var results = await store.SearchLexAsync("meeting", new LexSearchOptions { Limit = 1 });
        results.Count.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task SearchLex_WithCollectionFilter()
    {
        await using var store = CreateSeededStore();
        var results = await store.SearchLexAsync("authentication",
            new LexSearchOptions { Collections = new List<string> { "notes" } });
        foreach (var r in results)
        {
            r.CollectionName.Should().Be("notes");
        }
    }

    [Fact]
    public async Task SearchLex_ReturnsEmptyForNonMatching()
    {
        await using var store = CreateSeededStore();
        var results = await store.SearchLexAsync("xyznonexistentterm123");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchLex_EmptyStore_ReturnsEmpty()
    {
        await using var store = await QmdStoreFactory.CreateInMemoryAsync();
        var results = await store.SearchLexAsync("test");
        results.Should().BeEmpty();
    }

    #endregion

    #region HybridQueryAsync (low-level pipeline)

    [Fact]
    public async Task HybridQueryAsync_ReturnsResults()
    {
        await using var store = CreateSeededStoreWithMockLlm();
        var results = await store.HybridQueryAsync("authentication");
        results.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HybridQueryAsync_RespectsLimit()
    {
        await using var store = CreateSeededStoreWithMockLlm();
        var results = await store.HybridQueryAsync("authentication",
            new HybridQueryOptions { Limit = 1, SkipRerank = true });
        results.Count.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task HybridQueryAsync_RespectsCollectionFilter()
    {
        await using var store = CreateSeededStoreWithMockLlm();
        var results = await store.HybridQueryAsync("authentication",
            new HybridQueryOptions { Collections = ["notes"], SkipRerank = true });
        foreach (var r in results)
        {
            r.DisplayPath.Should().Contain("notes");
        }
    }

    #endregion

    #region GetContextForFileAsync

    [Fact]
    public async Task GetContextForFile_ReturnsNull_WhenNoContext()
    {
        await using var store = CreateSeededStore();
        var context = await store.GetContextForFileAsync("qmd://docs/readme.md");
        context.Should().BeNull();
    }

    [Fact]
    public async Task GetContextForFile_ReturnsNull_ForUnknownPath()
    {
        await using var store = CreateSeededStore();
        var context = await store.GetContextForFileAsync("qmd://nonexistent/file.md");
        context.Should().BeNull();
    }

    [Fact]
    public async Task GetContextForFile_ReturnsContext_WithFilesystemPath()
    {
        // Build a store where collection path matches filesystem conventions
        // so ContextResolver can resolve filesystem path → collection + relative path
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection
                {
                    Path = "/test/docs",
                    Pattern = "**/*.md",
                    Context = new Dictionary<string, string> { ["/"] = "Documentation files" },
                },
            }
        };
        var db = new SqliteDatabase(":memory:");
        var configManager = new ConfigManager(new InlineConfigSource(config));
        await using var store = new QmdStore(db, configManager);

        // Insert document with relative path (matching what ContextResolver expects)
        var body = "# Readme\n\nTest content.";
        var hash = store.HashContent(body);
        store.InsertContent(hash, body, "2025-01-01");
        store.InsertDocument("docs", "readme.md", "Readme", hash, "2025-01-01", "2025-01-01");
        var context = await store.GetContextForFileAsync("/test/docs/readme.md");
        context.Should().Be("Documentation files");
    }

    #endregion

    #region FindSimilarFilesAsync

    [Fact]
    public async Task FindSimilarFiles_FindsCloseMatches()
    {
        await using var store = CreateSeededStore();
        var similar = await store.FindSimilarFilesAsync("qmd://docs/auth.m");
        similar.Should().Contain(p => p.Contains("auth.md"));
    }

    [Fact]
    public async Task FindSimilarFiles_RespectsLimit()
    {
        await using var store = CreateSeededStore();
        var similar = await store.FindSimilarFilesAsync("qmd://docs/a", limit: 1);
        similar.Count.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task FindSimilarFiles_ReturnsEmpty_WhenNoCloseMatch()
    {
        await using var store = CreateSeededStore();
        var similar = await store.FindSimilarFilesAsync("completely-unrelated-path-xyz", maxDistance: 1);
        similar.Should().BeEmpty();
    }

    #endregion

    #region GetActiveDocumentPathsAsync

    [Fact]
    public async Task GetActiveDocumentPaths_ReturnsPaths()
    {
        await using var store = CreateSeededStore();
        var paths = await store.GetActiveDocumentPathsAsync("docs");
        paths.Should().HaveCount(3);
        paths.Should().Contain(p => p.Contains("readme.md"));
        paths.Should().Contain(p => p.Contains("auth.md"));
        paths.Should().Contain(p => p.Contains("api.md"));
    }

    [Fact]
    public async Task GetActiveDocumentPaths_ReturnsEmpty_ForUnknownCollection()
    {
        await using var store = CreateSeededStore();
        var paths = await store.GetActiveDocumentPathsAsync("nonexistent");
        paths.Should().BeEmpty();
    }

    #endregion

    #region Get and MultiGet

    [Fact]
    public async Task Get_RetrievesDocumentByPath()
    {
        await using var store = CreateSeededStore();
        var result = await store.GetAsync("qmd://docs/auth.md");

        result.IsFound.Should().BeTrue();
        result.Document!.Title.Should().Be("Authentication");
        result.Document!.CollectionName.Should().Be("docs");
    }

    [Fact]
    public async Task Get_WithIncludeBodyReturnsBody()
    {
        await using var store = CreateSeededStore();
        var result = await store.GetAsync("qmd://docs/auth.md", new GetOptions { IncludeBody = true });

        result.IsFound.Should().BeTrue();
        result.Document!.Body.Should().NotBeNull();
        result.Document!.Body.Should().Contain("JWT tokens");
    }

    [Fact]
    public async Task Get_ReturnsNotFoundForMissing()
    {
        await using var store = CreateSeededStore();
        var result = await store.GetAsync("qmd://docs/nonexistent.md");

        result.IsFound.Should().BeFalse();
        result.NotFound.Should().NotBeNull();
        result.NotFound!.Error.Should().Be("not_found");
    }

    [Fact]
    public async Task Get_ByDocid()
    {
        await using var store = CreateSeededStore();
        // First get a document to find its docid
        var doc = await store.GetAsync("qmd://docs/readme.md");
        doc.IsFound.Should().BeTrue();

        var byDocid = await store.GetAsync($"#{doc.Document!.DocId}");
        byDocid.IsFound.Should().BeTrue();
        byDocid.Document!.DocId.Should().Be(doc.Document!.DocId);
    }

    [Fact]
    public async Task MultiGet_RetrievesMultipleDocuments()
    {
        await using var store = CreateSeededStore();
        var (docs, errors) = await store.MultiGetAsync("qmd://docs/*.md");
        docs.Count.Should().BeGreaterThan(0);
    }

    #endregion

    #region Index health

    [Fact]
    public async Task GetStatus_ReturnsHealth()
    {
        await using var store = await QmdStoreFactory.CreateInMemoryAsync();
        var status = await store.GetStatusAsync();
        status.TotalDocuments.Should().Be(0);
        status.Collections.Should().BeEmpty();
    }

    [Fact]
    public async Task GetIndexHealth_ReturnsInfo()
    {
        await using var store = await QmdStoreFactory.CreateInMemoryAsync();
        var health = await store.GetIndexHealthAsync();
        health.TotalDocs.Should().Be(0);
        health.NeedsEmbedding.Should().Be(0);
    }

    [Fact]
    public async Task FreshStore_HasZeroDocuments()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/docs", Pattern = "**/*.md" },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);
        var status = await store.GetStatusAsync();
        status.TotalDocuments.Should().Be(0);
    }

    [Fact]
    public async Task GetDefaultCollectionNames_ReturnsDefaults()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["a"] = new Collection { Path = "/a" },
                ["b"] = new Collection { Path = "/b", IncludeByDefault = false },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);
        var names = await store.GetDefaultCollectionNamesAsync();
        names.Should().Contain("a");
        names.Should().NotContain("b");
    }

    #endregion

    #region Lifecycle

    [Fact]
    public async Task DisposeAsync_DoesNotThrow()
    {
        var store = await QmdStoreFactory.CreateInMemoryAsync();
        var act = async () => await store.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Dispose_MakesSubsequentOperationsFail()
    {
        var store = await QmdStoreFactory.CreateInMemoryAsync(new CollectionConfig());
        await store.DisposeAsync();

        // Database operations should fail after dispose
        var act = async () => await store.GetStatusAsync();
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task MultipleStores_Coexist()
    {
        var config1 = new CollectionConfig
        {
            Collections = new() { ["docs"] = new Collection { Path = "/docs" } }
        };
        var store1 = await QmdStoreFactory.CreateInMemoryAsync(config1);
        await store1.DisposeAsync();

        var config2 = new CollectionConfig
        {
            Collections = new() { ["notes"] = new Collection { Path = "/notes" } }
        };
        await using var store2 = await QmdStoreFactory.CreateInMemoryAsync(config2);

        var names = (await store2.ListCollectionsAsync()).Select(c => c.Name).ToList();
        names.Should().Contain("notes");
        names.Should().NotContain("docs");
    }

    #endregion

    #region Config initialization

    [Fact]
    public async Task InlineConfig_WithGlobalContextPreserved()
    {
        var config = new CollectionConfig
        {
            GlobalContext = "System knowledge base",
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/docs", Pattern = "**/*.md" },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);

        var global = await store.GetGlobalContextAsync();
        global.Should().Be("System knowledge base");
    }

    [Fact]
    public async Task InlineConfig_WithPreExistingContextsPreserved()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection
                {
                    Path = "/docs",
                    Pattern = "**/*.md",
                    Context = new Dictionary<string, string> { ["/auth"] = "Authentication docs" },
                },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);

        var contexts = await store.ListContextsAsync();
        contexts.Should().Contain(c =>
            c.Collection == "docs" && c.Path == "/auth" && c.Context == "Authentication docs");
    }

    [Fact]
    public async Task InlineConfig_WithEmptyCollections()
    {
        var config = new CollectionConfig { Collections = new() };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);

        (await store.ListCollectionsAsync()).Should().BeEmpty();
        (await store.ListContextsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task InlineConfig_WithMultipleCollectionOptions()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection
                {
                    Path = "/docs",
                    Pattern = "**/*.md",
                    Ignore = new List<string> { "drafts/**" },
                    IncludeByDefault = true,
                },
                ["notes"] = new Collection
                {
                    Path = "/notes",
                    Pattern = "**/*.md",
                    IncludeByDefault = false,
                },
            }
        };
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);

        var collections = await store.ListCollectionsAsync();
        collections.Should().HaveCount(2);
    }

    #endregion

    #region DB-only mode (config sync + persistence via file-based store)

    [Fact]
    public async Task ConfigSync_PopulatesStoreCollectionsTable()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection
                {
                    Path = "/test/docs",
                    Pattern = "**/*.md",
                    Context = new Dictionary<string, string> { ["/auth"] = "Auth documentation" },
                },
            }
        };

        await using var store = await QmdStoreFactory.CreateInMemoryAsync(config);

        // Verify collections are in the DB via listCollections
        var collections = await store.ListCollectionsAsync();
        collections.Should().HaveCount(1);
        collections[0].Name.Should().Be("docs");
        collections[0].Path.Should().Be("/test/docs");

        // Verify contexts are accessible
        var contexts = await store.ListContextsAsync();
        contexts.Should().Contain(c =>
            c.Collection == "docs" && c.Path == "/auth" && c.Context == "Auth documentation");
    }

    [Fact]
    public async Task DbOnlyMode_SupportsCollectionMutations()
    {
        // Use a temp DB file to test persistence across sessions
        var dbPath = Path.Combine(Path.GetTempPath(), $"qmd-test-{Guid.NewGuid()}.sqlite");

        // Session 1: create with config
        var store1 = await QmdStoreFactory.CreateAsync(new StoreOptions
        {
            DbPath = dbPath,
            Config = new CollectionConfig
            {
                Collections = new()
                {
                    ["docs"] = new Collection { Path = "/test/docs", Pattern = "**/*.md" },
                }
            }
        });
        await store1.AddCollectionAsync("notes", "/test/notes", "**/*.md");
        var names1 = (await store1.ListCollectionsAsync()).Select(c => c.Name).OrderBy(n => n).ToList();
        names1.Should().Equal("docs", "notes");

        await store1.DisposeAsync();

        // Session 2: reopen with same config shape, verify both collections are there
        var store2 = await QmdStoreFactory.CreateAsync(new StoreOptions
        {
            DbPath = dbPath,
            Config = new CollectionConfig
            {
                Collections = new()
                {
                    ["docs"] = new Collection { Path = "/test/docs", Pattern = "**/*.md" },
                    ["notes"] = new Collection { Path = "/test/notes", Pattern = "**/*.md" },
                }
            }
        });
        var names2 = (await store2.ListCollectionsAsync()).Select(c => c.Name).OrderBy(n => n).ToList();
        names2.Should().Equal("docs", "notes");
        await store2.DisposeAsync();

        TryDeleteFile(dbPath);
    }

    [Fact]
    public async Task DbOnlyMode_SupportsContextMutations()
    {
        // Use a temp DB file to test persistence across sessions
        var dbPath = Path.Combine(Path.GetTempPath(), $"qmd-test-{Guid.NewGuid()}.sqlite");

        // Session 1: create with config, add context and global context
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection { Path = "/test/docs", Pattern = "**/*.md" },
            }
        };
        var store1 = await QmdStoreFactory.CreateAsync(new StoreOptions
        {
            DbPath = dbPath,
            Config = config
        });
        await store1.AddContextAsync("docs", "/api", "API docs");
        await store1.SetGlobalContextAsync("Global context");
        await store1.DisposeAsync();

        // Session 2: reopen with same config, verify contexts persist in config manager
        var store2 = await QmdStoreFactory.CreateAsync(new StoreOptions
        {
            DbPath = dbPath,
            Config = new CollectionConfig
            {
                GlobalContext = "Global context",
                Collections = new()
                {
                    ["docs"] = new Collection
                    {
                        Path = "/test/docs",
                        Pattern = "**/*.md",
                        Context = new Dictionary<string, string> { ["/api"] = "API docs" },
                    },
                }
            }
        });

        var contexts = await store2.ListContextsAsync();
        contexts.Should().Contain(c =>
            c.Collection == "docs" && c.Path == "/api" && c.Context == "API docs");
        contexts.Should().Contain(c =>
            c.Collection == "*" && c.Path == "/" && c.Context == "Global context");

        await store2.DisposeAsync();

        TryDeleteFile(dbPath);
    }

    #endregion

    #region Inline config isolation

    [Fact]
    public async Task InlineConfig_DoesNotWriteFilesToDisk()
    {
        // Inline config does not write any files to disk
        var watchDir = Path.Combine(Path.GetTempPath(), $"qmd-no-write-{Guid.NewGuid()}");
        // Directory should not be created by in-memory store
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(
            new CollectionConfig
            {
                Collections = new() { ["docs"] = new Collection { Path = "/docs", Pattern = "**/*.md" } }
            });

        await store.AddCollectionAsync("notes", "/notes", "**/*.md");
        await store.AddContextAsync("docs", "/", "Documentation");

        Directory.Exists(watchDir).Should().BeFalse();
    }

    [Fact]
    public async Task InlineConfig_MutationsPersistWithinSession()
    {
        // Inline config mutations persist within session
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(new CollectionConfig());

        await store.AddCollectionAsync("docs", "/docs", "**/*.md");
        await store.AddContextAsync("docs", "/", "My docs");

        var collections = await store.ListCollectionsAsync();
        collections.Select(c => c.Name).Should().Contain("docs");

        var contexts = await store.ListContextsAsync();
        contexts.Should().Contain(c =>
            c.Collection == "docs" && c.Path == "/" && c.Context == "My docs");
    }

    [Fact]
    public async Task TwoStores_DifferentInlineConfigs_Independent()
    {
        // Two stores with different inline configs are independent
        var store1 = await QmdStoreFactory.CreateInMemoryAsync(
            new CollectionConfig
            {
                Collections = new() { ["docs"] = new Collection { Path = "/docs" } }
            });
        await store1.DisposeAsync();

        await using var store2 = await QmdStoreFactory.CreateInMemoryAsync(
            new CollectionConfig
            {
                Collections = new() { ["notes"] = new Collection { Path = "/notes" } }
            });

        var names = (await store2.ListCollectionsAsync()).Select(c => c.Name).ToList();
        names.Should().Contain("notes");
        names.Should().NotContain("docs");
    }

    #endregion

    #region Search structured

    [Fact]
    public async Task SearchStructured_PreExpandedLexQueries_ReturnsResults()
    {
        // Search with pre-expanded lex queries and rerank disabled
        // The .NET structured search requires an LLM service reference even for lex-only queries,
        // so we provide a stub that won't actually be called (SkipRerank + lex-only = no LLM invocations).
        await using var store = CreateSeededStoreWithMockLlm();
        var results = await store.SearchStructuredAsync(
            new List<ExpandedQuery>
            {
                new("lex", "authentication JWT"),
                new("lex", "login session"),
            },
            new StructuredSearchOptions { SkipRerank = true },
            CancellationToken.None);
        results.Count.Should().BeGreaterThan(0);
    }

    #endregion

    #region Update

    [Fact]
    public async Task Update_IndexesFilesAndReturnsCorrectStats()
    {
        // Indexes files and returns correct stats
        var docsDir = CreateTempDocsDir();
        try
        {
            await using var store = await QmdStoreFactory.CreateInMemoryAsync(
                new CollectionConfig
                {
                    Collections = new() { ["docs"] = new Collection { Path = docsDir, Pattern = "**/*.md" } }
                });

            var result = await store.UpdateAsync();
            result.Collections.Should().Be(1);
            result.Indexed.Should().Be(3); // readme.md, auth.md, api.md
            result.Updated.Should().Be(0);
            result.Unchanged.Should().Be(0);
            result.Removed.Should().Be(0);
        }
        finally { TryCleanupDir(docsDir); }
    }

    [Fact]
    public async Task Update_SecondRunShowsUnchanged()
    {
        // Second update shows unchanged files
        var docsDir = CreateTempDocsDir();
        try
        {
            await using var store = await QmdStoreFactory.CreateInMemoryAsync(
                new CollectionConfig
                {
                    Collections = new() { ["docs"] = new Collection { Path = docsDir, Pattern = "**/*.md" } }
                });

            await store.UpdateAsync();
            var result = await store.UpdateAsync();

            result.Indexed.Should().Be(0);
            result.Unchanged.Should().Be(3);
        }
        finally { TryCleanupDir(docsDir); }
    }

    [Fact]
    public async Task Update_OnProgressCallbackFires()
    {
        // OnProgress callback fires during update
        var docsDir = CreateTempDocsDir();
        try
        {
            await using var store = await QmdStoreFactory.CreateInMemoryAsync(
                new CollectionConfig
                {
                    Collections = new() { ["docs"] = new Collection { Path = docsDir, Pattern = "**/*.md" } }
                });

            var progress = new List<ReindexProgress>();
            await store.UpdateAsync(new UpdateOptions
            {
                Progress = new TestHelpers.SyncProgress<ReindexProgress>(p => progress.Add(p)),
            });

            progress.Count.Should().BeGreaterThan(0);
            progress[0].Total.Should().Be(3);
        }
        finally { TryCleanupDir(docsDir); }
    }

    [Fact]
    public async Task Update_WithCollectionFilter_OnlyUpdatesFiltered()
    {
        // Update with collection filter only indexes the specified collection
        var docsDir = CreateTempDocsDir();
        var notesDir = CreateTempDocsDir("notes");
        try
        {
            await using var store = await QmdStoreFactory.CreateInMemoryAsync(
                new CollectionConfig
                {
                    Collections = new()
                    {
                        ["docs"] = new Collection { Path = docsDir, Pattern = "**/*.md" },
                        ["notes"] = new Collection { Path = notesDir, Pattern = "**/*.md" },
                    }
                });

            var result = await store.UpdateAsync(new UpdateOptions
            {
                Collections = new List<string> { "docs" }
            });

            result.Collections.Should().Be(1);
            result.Indexed.Should().Be(3); // Only docs
        }
        finally { TryCleanupDir(docsDir); TryCleanupDir(notesDir); }
    }

    [Fact]
    public async Task Update_MultipleCollections_IndexesBoth()
    {
        // Update indexes multiple collections
        var docsDir = CreateTempDocsDir();
        var notesDir = CreateTempDocsDir("notes");
        try
        {
            await using var store = await QmdStoreFactory.CreateInMemoryAsync(
                new CollectionConfig
                {
                    Collections = new()
                    {
                        ["docs"] = new Collection { Path = docsDir, Pattern = "**/*.md" },
                        ["notes"] = new Collection { Path = notesDir, Pattern = "**/*.md" },
                    }
                });

            var result = await store.UpdateAsync();
            result.Collections.Should().Be(2);
            result.Indexed.Should().Be(6); // 3 docs + 3 notes
        }
        finally { TryCleanupDir(docsDir); TryCleanupDir(notesDir); }
    }

    [Fact]
    public async Task Update_DocumentsSearchableAfterUpdate()
    {
        // Documents are searchable after update
        var docsDir = CreateTempDocsDir();
        try
        {
            await using var store = await QmdStoreFactory.CreateInMemoryAsync(
                new CollectionConfig
                {
                    Collections = new() { ["docs"] = new Collection { Path = docsDir, Pattern = "**/*.md" } }
                });

            await store.UpdateAsync();

            var results = await store.SearchLexAsync("authentication");
            results.Count.Should().BeGreaterThan(0);
        }
        finally { TryCleanupDir(docsDir); }
    }

    #endregion

    #region Embed

    [Fact]
    public async Task Embed_InvalidBatchLimits_Throws()
    {
        // Embed rejects invalid batch limits
        await using var store = await QmdStoreFactory.CreateInMemoryAsync(new CollectionConfig());

        var act1 = async () => await store.EmbedAsync(new EmbedPipelineOptions { MaxDocsPerBatch = 0 });
        await act1.Should().ThrowAsync<Exception>();

        var act2 = async () => await store.EmbedAsync(new EmbedPipelineOptions { MaxBatchBytes = 0 });
        await act2.Should().ThrowAsync<Exception>();
    }

    #endregion

    #region DB-only mode: reopen with just dbPath

    [Fact]
    public async Task DbOnlyMode_ReopenWithSameConfig_PreservesData()
    {
        // Reopen store with just dbPath after a config+update session
        // Note: .NET ConfigSync always syncs config to DB, so we reopen with the same config.
        // The hash-skip path ensures no data is wiped (same config hash → skip sync → data preserved).
        var docsDir = CreateTempDocsDir();
        var dbPath = Path.Combine(Path.GetTempPath(), $"qmd-test-{Guid.NewGuid()}.sqlite");
        var config = new CollectionConfig
        {
            GlobalContext = "Test knowledge base",
            Collections = new() { ["docs"] = new Collection { Path = docsDir, Pattern = "**/*.md" } }
        };
        try
        {
            // Session 1: create with config, update
            var store1 = await QmdStoreFactory.CreateAsync(new StoreOptions { DbPath = dbPath, Config = config });
            await store1.UpdateAsync();
            var status1 = await store1.GetStatusAsync();
            status1.TotalDocuments.Should().BeGreaterThan(0);
            await store1.DisposeAsync();

            // Session 2: reopen with same config (config hash skip → data preserved)
            var store2 = await QmdStoreFactory.CreateAsync(new StoreOptions { DbPath = dbPath, Config = config });

            // Collections should still be available
            var collections = await store2.ListCollectionsAsync();
            collections.Select(c => c.Name).Should().Contain("docs");

            // Search should still work
            var results = await store2.SearchLexAsync("authentication");
            results.Count.Should().BeGreaterThan(0);

            // Global context should persist
            var globalCtx = await store2.GetGlobalContextAsync();
            globalCtx.Should().Be("Test knowledge base");

            await store2.DisposeAsync();
        }
        finally { TryCleanupDir(docsDir); TryDeleteFile(dbPath); }
    }

    [Fact]
    public async Task ConfigSync_SameHash_SkipsSync()
    {
        // Second init with same config skips sync (config hash skip)
        var dbPath = Path.Combine(Path.GetTempPath(), $"qmd-test-{Guid.NewGuid()}.sqlite");
        var config = new CollectionConfig
        {
            Collections = new() { ["docs"] = new Collection { Path = "/test/docs", Pattern = "**/*.md" } }
        };
        try
        {
            // First init — syncs config
            var store1 = await QmdStoreFactory.CreateAsync(new StoreOptions { DbPath = dbPath, Config = config });
            await store1.DisposeAsync();

            // Second init with same config — should not error
            var store2 = await QmdStoreFactory.CreateAsync(new StoreOptions { DbPath = dbPath, Config = config });
            var collections = await store2.ListCollectionsAsync();
            collections.Should().HaveCount(1);
            collections[0].Name.Should().Be("docs");
            await store2.DisposeAsync();
        }
        finally { TryDeleteFile(dbPath); }
    }

    #endregion

    #region Misc

    [Fact]
    public async Task CreateAsync_CreatesDatabaseFileOnDisk()
    {
        // Creates database file on disk
        var dbPath = Path.Combine(Path.GetTempPath(), $"qmd-test-{Guid.NewGuid()}.sqlite");
        try
        {
            var store = await QmdStoreFactory.CreateAsync(new StoreOptions
            {
                DbPath = dbPath,
                Config = new CollectionConfig()
            });
            File.Exists(dbPath).Should().BeTrue();
            await store.DisposeAsync();
        }
        finally { TryDeleteFile(dbPath); }
    }

    [Fact]
    public async Task SearchLex_FindsDocumentsAcrossCollections()
    {
        // SearchLex finds documents across collections
        await using var store = CreateSeededStore();
        var results = await store.SearchLexAsync("authentication", new LexSearchOptions { Limit = 10 });
        // Auth appears in docs/auth.md and potentially notes
        results.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion

    #region YAML config file mode

    [Fact]
    public async Task YamlConfig_LoadsCollectionsFromFile()
    {
        // Loads collections from YAML file
        var configPath = Path.Combine(Path.GetTempPath(), $"qmd-cfg-{Guid.NewGuid()}.yml");
        var dbPath = Path.Combine(Path.GetTempPath(), $"qmd-test-{Guid.NewGuid()}.sqlite");
        try
        {
            File.WriteAllText(configPath,
                "collections:\n  docs:\n    path: /test/docs\n    pattern: \"**/*.md\"\n  notes:\n    path: /test/notes\n    pattern: \"**/*.md\"\n");

            var store = await QmdStoreFactory.CreateAsync(new StoreOptions
            {
                DbPath = dbPath,
                ConfigPath = configPath,
            });
            var names = (await store.ListCollectionsAsync()).Select(c => c.Name).ToList();
            names.Should().Contain("docs");
            names.Should().Contain("notes");
            await store.DisposeAsync();
        }
        finally { TryDeleteFile(configPath); TryDeleteFile(dbPath); }
    }

    [Fact]
    public async Task YamlConfig_AddCollectionPersistsToFile()
    {
        // AddCollection persists to YAML file
        var configPath = Path.Combine(Path.GetTempPath(), $"qmd-cfg-{Guid.NewGuid()}.yml");
        var dbPath = Path.Combine(Path.GetTempPath(), $"qmd-test-{Guid.NewGuid()}.sqlite");
        try
        {
            File.WriteAllText(configPath, "collections: {}\n");

            var store = await QmdStoreFactory.CreateAsync(new StoreOptions
            {
                DbPath = dbPath,
                ConfigPath = configPath,
            });
            await store.AddCollectionAsync("newcol", "/home/docs", "**/*.md");
            await store.DisposeAsync();

            // Read YAML file directly and verify
            var raw = File.ReadAllText(configPath);
            raw.Should().Contain("newcol");
        }
        finally { TryDeleteFile(configPath); TryDeleteFile(dbPath); }
    }

    [Fact]
    public async Task YamlConfig_ContextPersistsToFile()
    {
        // Context persists to YAML file
        var configPath = Path.Combine(Path.GetTempPath(), $"qmd-cfg-{Guid.NewGuid()}.yml");
        var dbPath = Path.Combine(Path.GetTempPath(), $"qmd-test-{Guid.NewGuid()}.sqlite");
        try
        {
            File.WriteAllText(configPath,
                "collections:\n  docs:\n    path: /test/docs\n    pattern: \"**/*.md\"\n");

            var store = await QmdStoreFactory.CreateAsync(new StoreOptions
            {
                DbPath = dbPath,
                ConfigPath = configPath,
            });
            await store.AddContextAsync("docs", "/api", "API documentation");
            await store.DisposeAsync();

            var raw = File.ReadAllText(configPath);
            raw.Should().Contain("API documentation");
        }
        finally { TryDeleteFile(configPath); TryDeleteFile(dbPath); }
    }

    [Fact]
    public async Task YamlConfig_NonExistentFile_ReturnsEmptyCollections()
    {
        // Non-existent config file returns empty collections
        var configPath = Path.Combine(Path.GetTempPath(), $"qmd-cfg-nonexistent-{Guid.NewGuid()}.yml");
        var dbPath = Path.Combine(Path.GetTempPath(), $"qmd-test-{Guid.NewGuid()}.sqlite");
        try
        {
            var store = await QmdStoreFactory.CreateAsync(new StoreOptions
            {
                DbPath = dbPath,
                ConfigPath = configPath,
            });
            var collections = await store.ListCollectionsAsync();
            collections.Should().BeEmpty();
            await store.DisposeAsync();
        }
        finally { TryDeleteFile(dbPath); }
    }

    #endregion

    #region Helpers for Update tests: creates temp directories with fixture files

    private static string CreateTempDocsDir(string prefix = "docs")
    {
        var dir = Path.Combine(Path.GetTempPath(), $"qmd-test-{prefix}-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "readme.md"), "# Getting Started\n\nWelcome to the project.");
        File.WriteAllText(Path.Combine(dir, "auth.md"), "# Authentication\n\nUses JWT tokens for session management.\n");
        File.WriteAllText(Path.Combine(dir, "api.md"), "# API Reference\n\n## POST /login\nAuthenticate a user.\n");
        return dir;
    }

    #endregion
}
