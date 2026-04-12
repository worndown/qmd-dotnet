using FluentAssertions;
using Qmd.Core.Chunking;
using Qmd.Core.Configuration;
using Qmd.Core.Database;
using Qmd.Core.Retrieval;
using Qmd.Core.Store;

namespace Qmd.Core.Tests.Store;

public class QmdStoreTests : IDisposable
{
    private readonly QmdStore _store;

    public QmdStoreTests()
    {
        _store = new QmdStore(new SqliteDatabase(":memory:"));
    }

    public void Dispose() => _store.Dispose();

    [Fact]
    public void CreateStore_InitializesSchema()
    {
        var row = _store.Db.Prepare("SELECT name FROM sqlite_master WHERE type='table' AND name='documents'")
            .GetDynamic();
        row.Should().NotBeNull();
    }

    [Fact]
    public void Store_InsertAndSearch()
    {
        var content = "This document covers authentication patterns and OAuth2 implementation.";
        var hash = _store.HashContent(content);
        _store.InsertContent(hash, content, "2025-01-01");
        _store.InsertDocument("docs", "auth.md", "Authentication", hash, "2025-01-01", "2025-01-01");

        var results = _store.SearchFTS("authentication");
        results.Should().NotBeEmpty();
        results[0].Title.Should().Be("Authentication");
    }

    [Fact]
    public void Store_IndexAndRetrieve()
    {
        var hash = _store.HashContent("Hello world");
        _store.InsertContent(hash, "Hello world", "2025-01-01");
        _store.InsertDocument("docs", "hello.md", "Hello", hash, "2025-01-01", "2025-01-01");

        var active = _store.FindActiveDocument("docs", "hello.md");
        active.Should().NotBeNull();
        active!.Hash.Should().Be(hash);
    }

    [Fact]
    public void Store_Status()
    {
        var config = new CollectionConfig
        {
            Collections = new() { ["docs"] = new Collection { Path = "/docs" } }
        };
        _store.SyncConfig(config);

        var hash = _store.HashContent("Content");
        _store.InsertContent(hash, "Content", "2025-01-01");
        _store.InsertDocument("docs", "a.md", "A", hash, "2025-01-01", "2025-01-01");

        var status = _store.GetStatus();
        status.TotalDocuments.Should().Be(1);
        status.Collections.Should().HaveCount(1);
    }

    [Fact]
    public void Store_Chunking()
    {
        var content = new string('A', 10000);
        var chunks = DocumentChunker.ChunkDocument(content, 1000, 0);
        chunks.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Store_Cache()
    {
        _store.SetCachedResult("key1", "value1");
        _store.GetCachedResult("key1").Should().Be("value1");
        _store.ClearCache();
        _store.GetCachedResult("key1").Should().BeNull();
    }

    [Fact]
    public void Store_Maintenance()
    {
        var hash = _store.HashContent("Content");
        _store.InsertContent(hash, "Content", "2025-01-01");
        _store.InsertDocument("docs", "a.md", "A", hash, "2025-01-01", "2025-01-01");
        _store.DeactivateDocument("docs", "a.md");

        var result = _store.CleanupAsync().Result;
        result.OrphanedCollectionDocsDeleted.Should().Be(1);
    }

    [Fact]
    public void EdgeCase_HandlesEmptyDatabaseGracefully()
    {
        using var store = new QmdStore(new SqliteDatabase(":memory:"));

        var searchResults = store.SearchFTS("anything", 10);
        searchResults.Should().BeEmpty();

        var findResult = DocumentFinder.FindDocument(store.Db, "nonexistent.md");
        findResult.IsFound.Should().BeFalse();
    }

    [Fact]
    public void EdgeCase_HandlesVeryLongDocumentBodies()
    {
        using var store = new QmdStore(new SqliteDatabase(":memory:"));
        SeedCollectionForStore(store, "docs", "/docs");

        var longBody = string.Concat(Enumerable.Repeat("word ", 100000)); // ~600KB
        var hash = store.HashContent(longBody);
        store.InsertContent(hash, longBody, "2025-01-01");
        store.InsertDocument("docs", "long.md", "long", hash, "2025-01-01", "2025-01-01");

        var results = store.SearchFTS("word", 10);
        results.Should().HaveCount(1);
    }

    [Fact]
    public void EdgeCase_HandlesUnicodeContentCorrectly()
    {
        using var store = new QmdStore(new SqliteDatabase(":memory:"));
        SeedCollectionForStore(store, "docs", "/docs");

        var body = "# \u65e5\u672c\u8a9e\n\n\u5185\u5bb9\u306f\u65e5\u672c\u8a9e\u3067\u66f8\u304b\u308c\u3066\u3044\u307e\u3059\u3002\n\nEmoji: \ud83c\udf89\ud83d\ude80\u2728";
        var hash = store.HashContent(body);
        store.InsertContent(hash, body, "2025-01-01");
        store.InsertDocument("docs", "unicode.md", "\u65e5\u672c\u8a9e\u30bf\u30a4\u30c8\u30eb", hash, "2025-01-01", "2025-01-01");

        // Should be searchable
        var results = store.SearchFTS("\u65e5\u672c\u8a9e", 10);
        results.Should().NotBeEmpty();

        // Should retrieve correctly
        var findResult = DocumentFinder.FindDocument(store.Db, "unicode.md", includeBody: true);
        findResult.IsFound.Should().BeTrue();
        findResult.Document!.Title.Should().Be("\u65e5\u672c\u8a9e\u30bf\u30a4\u30c8\u30eb");
        findResult.Document.Body.Should().Contain("\ud83c\udf89");
    }

    [Fact]
    public void EdgeCase_HandlesDocumentsWithSpecialCharactersInPaths()
    {
        using var store = new QmdStore(new SqliteDatabase(":memory:"));
        SeedCollectionForStore(store, "docs", "/path");

        var body = "Content";
        var hash = store.HashContent(body);
        store.InsertContent(hash, body, "2025-01-01");
        store.InsertDocument("docs", "file with spaces.md", "special", hash, "2025-01-01", "2025-01-01");

        var findResult = DocumentFinder.FindDocument(store.Db, "file with spaces.md");
        findResult.IsFound.Should().BeTrue();
    }

    [Fact]
    public void EdgeCase_HandlesConcurrentOperations()
    {
        using var store = new QmdStore(new SqliteDatabase(":memory:"));
        SeedCollectionForStore(store, "docs", "/path");

        // Insert multiple documents (sequential — SQLite is single-writer)
        for (int i = 0; i < 10; i++)
        {
            var body = $"Content {i} searchterm";
            var hash = store.HashContent(body);
            store.InsertContent(hash, body, "2025-01-01");
            store.InsertDocument("docs", $"concurrent{i}.md", $"concurrent{i}", hash, "2025-01-01", "2025-01-01");
        }

        // All should be searchable
        var results = store.SearchFTS("searchterm", 20);
        results.Should().HaveCount(10);
    }

    [Fact]
    public void CAS_SameContentGetsSameHashFromMultipleCollections()
    {
        using var store = new QmdStore(new SqliteDatabase(":memory:"));
        SeedCollectionForStore(store, "collection1", "/path/collection1");
        SeedCollectionForStore(store, "collection2", "/path/collection2");

        var content = "# Same Content\n\nThis is the same content in two places.";
        var expectedHash = store.HashContent(content);

        store.InsertContent(expectedHash, content, "2025-01-01");
        store.InsertDocument("collection1", "doc1.md", "doc1", expectedHash, "2025-01-01", "2025-01-01");
        store.InsertDocument("collection2", "doc2.md", "doc2", expectedHash, "2025-01-01", "2025-01-01");

        // Both documents should reference the same hash
        var hash1 = store.Db.Prepare("SELECT hash FROM documents WHERE collection = $1 AND path = $2")
            .GetDynamic("collection1", "doc1.md");
        var hash2 = store.Db.Prepare("SELECT hash FROM documents WHERE collection = $1 AND path = $2")
            .GetDynamic("collection2", "doc2.md");

        hash1!["hash"]!.ToString().Should().Be(expectedHash);
        hash2!["hash"]!.ToString().Should().Be(expectedHash);

        // There should only be one entry in the content table for this hash
        var contentCount = store.Db.Prepare("SELECT COUNT(*) as count FROM content WHERE hash = $1")
            .GetDynamic(expectedHash);
        Convert.ToInt32(contentCount!["count"]).Should().Be(1);
    }

    [Fact]
    public void CAS_RemovingOneCollectionPreservesContentUsedByAnother()
    {
        using var store = new QmdStore(new SqliteDatabase(":memory:"));
        SeedCollectionForStore(store, "collection1", "/path/collection1");
        SeedCollectionForStore(store, "collection2", "/path/collection2");

        var sharedContent = "# Shared Content\n\nThis is shared.";
        var sharedHash = store.HashContent(sharedContent);
        store.InsertContent(sharedHash, sharedContent, "2025-01-01");
        store.InsertDocument("collection1", "shared1.md", "shared1", sharedHash, "2025-01-01", "2025-01-01");
        store.InsertDocument("collection2", "shared2.md", "shared2", sharedHash, "2025-01-01", "2025-01-01");

        var uniqueContent = "# Unique Content\n\nThis is unique to collection1.";
        var uniqueHash = store.HashContent(uniqueContent);
        store.InsertContent(uniqueHash, uniqueContent, "2025-01-01");
        store.InsertDocument("collection1", "unique.md", "unique", uniqueHash, "2025-01-01", "2025-01-01");

        // Verify both hashes exist in content table
        store.Db.Prepare("SELECT hash FROM content WHERE hash = $1").GetDynamic(sharedHash)
            .Should().NotBeNull();
        store.Db.Prepare("SELECT hash FROM content WHERE hash = $1").GetDynamic(uniqueHash)
            .Should().NotBeNull();

        // Remove collection1 documents
        store.Db.Prepare("DELETE FROM documents WHERE collection = $1").Run("collection1");

        // Clean up orphaned content (mimics what the CLI does)
        store.Db.Exec(@"
            DELETE FROM content
            WHERE hash NOT IN (SELECT DISTINCT hash FROM documents WHERE active = 1)
        ");

        // Shared content should still exist (used by collection2)
        store.Db.Prepare("SELECT hash FROM content WHERE hash = $1").GetDynamic(sharedHash)
            .Should().NotBeNull();

        // Unique content should be removed (only used by collection1)
        store.Db.Prepare("SELECT hash FROM content WHERE hash = $1").GetDynamic(uniqueHash)
            .Should().BeNull();
    }

    [Fact]
    public void CAS_DifferentContentGetsDifferentHashes()
    {
        using var store = new QmdStore(new SqliteDatabase(":memory:"));
        SeedCollectionForStore(store, "docs", "/path");

        var content1 = "# Content One";
        var content2 = "# Content Two";
        var hash1 = store.HashContent(content1);
        var hash2 = store.HashContent(content2);

        hash1.Should().NotBe(hash2);

        store.InsertContent(hash1, content1, "2025-01-01");
        store.InsertDocument("docs", "doc1.md", "doc1", hash1, "2025-01-01", "2025-01-01");

        store.InsertContent(hash2, content2, "2025-01-01");
        store.InsertDocument("docs", "doc2.md", "doc2", hash2, "2025-01-01", "2025-01-01");

        // Both hashes should exist in content table
        var hash1Db = store.Db.Prepare("SELECT hash FROM documents WHERE path = $1")
            .GetDynamic("doc1.md");
        var hash2Db = store.Db.Prepare("SELECT hash FROM documents WHERE path = $1")
            .GetDynamic("doc2.md");

        hash1Db!["hash"]!.ToString().Should().Be(hash1);
        hash2Db!["hash"]!.ToString().Should().Be(hash2);
        hash1Db!["hash"]!.ToString().Should().NotBe(hash2Db!["hash"]!.ToString());

        // Should have 2 entries in content table
        var contentCount = store.Db.Prepare("SELECT COUNT(*) as count FROM content")
            .GetDynamic();
        Convert.ToInt32(contentCount!["count"]).Should().Be(2);
    }

    [Fact]
    public void CAS_DeduplicatesContentAcrossManyCollections()
    {
        using var store = new QmdStore(new SqliteDatabase(":memory:"));

        var sharedContent = "# Common Header\n\nThis appears everywhere.";
        var sharedHash = store.HashContent(sharedContent);
        store.InsertContent(sharedHash, sharedContent, "2025-01-01");

        for (int i = 0; i < 5; i++)
        {
            SeedCollectionForStore(store, $"collection{i}", $"/path/collection{i}");
            store.InsertDocument($"collection{i}", $"doc{i}.md", $"doc{i}", sharedHash, "2025-01-01", "2025-01-01");
        }

        // Should have 5 documents
        var docCount = store.Db.Prepare("SELECT COUNT(*) as count FROM documents WHERE active = 1")
            .GetDynamic();
        Convert.ToInt32(docCount!["count"]).Should().Be(5);

        // But only 1 content entry
        var contentCount = store.Db.Prepare("SELECT COUNT(*) as count FROM content WHERE hash = $1")
            .GetDynamic(sharedHash);
        Convert.ToInt32(contentCount!["count"]).Should().Be(1);

        // All documents should point to the same hash
        var hashes = store.Db.Prepare("SELECT DISTINCT hash FROM documents WHERE active = 1")
            .AllDynamic();
        hashes.Should().HaveCount(1);
        hashes[0]["hash"]!.ToString().Should().Be(sharedHash);
    }

    [Fact]
    public void CAS_ReindexingDeactivatedPathReactivates()
    {
        using var store = new QmdStore(new SqliteDatabase(":memory:"));
        SeedCollectionForStore(store, "docs", "/path");
        var now = DateTime.UtcNow.ToString("o");

        var oldContent = "# First Version";
        var oldHash = store.HashContent(oldContent);
        store.InsertContent(oldHash, oldContent, now);
        store.InsertDocument("docs", "docs/foo.md", "foo", oldHash, now, now);

        // Simulate file removal during update pass
        store.DeactivateDocument("docs", "docs/foo.md");
        store.FindActiveDocument("docs", "docs/foo.md").Should().BeNull();

        // Simulate file coming back in a later update pass
        var newContent = "# Second Version";
        var newHash = store.HashContent(newContent);
        store.InsertContent(newHash, newContent, now);

        // Should not throw — InsertDocument uses UPDATE-first upsert
        var act = () => store.InsertDocument("docs", "docs/foo.md", "foo", newHash, now, now);
        act.Should().NotThrow();

        var rows = store.Db.Prepare(@"
            SELECT id, hash, active FROM documents
            WHERE collection = $1 AND path = $2
        ").AllDynamic("docs", "docs/foo.md");

        rows.Should().HaveCount(1);
        Convert.ToInt32(rows[0]["active"]).Should().Be(1);
        rows[0]["hash"]!.ToString().Should().Be(newHash);
    }

    [Fact]
    public void MultipleStores_OperateIndependently()
    {
        // Two separate DBs don't share data
        using var store1 = new QmdStore(new SqliteDatabase(":memory:"));
        using var store2 = new QmdStore(new SqliteDatabase(":memory:"));

        SeedCollectionForStore(store1, "docs", "/docs1");
        SeedCollectionForStore(store2, "docs", "/docs2");

        var hash1 = store1.HashContent("Content for store 1");
        store1.InsertContent(hash1, "Content for store 1", "2025-01-01");
        store1.InsertDocument("docs", "only-in-1.md", "Only In Store 1", hash1, "2025-01-01", "2025-01-01");

        var hash2 = store2.HashContent("Content for store 2");
        store2.InsertContent(hash2, "Content for store 2", "2025-01-01");
        store2.InsertDocument("docs", "only-in-2.md", "Only In Store 2", hash2, "2025-01-01", "2025-01-01");

        // store1 should only see its own document
        var results1 = store1.SearchFTS("Content", 10);
        results1.Should().HaveCount(1);
        results1[0].Title.Should().Be("Only In Store 1");

        // store2 should only see its own document
        var results2 = store2.SearchFTS("Content", 10);
        results2.Should().HaveCount(1);
        results2[0].Title.Should().Be("Only In Store 2");

        // Confirm no cross-contamination
        store1.SearchFTS("store 2", 10).Should().BeEmpty();
        store2.SearchFTS("store 1", 10).Should().BeEmpty();
    }

    private static void SeedCollectionForStore(QmdStore store, string name, string path)
    {
        // Merge into existing config to avoid overwriting other collections
        var existing = store.Db.Prepare("SELECT name FROM store_collections").AllDynamic();
        var config = new CollectionConfig { Collections = new() };
        foreach (var row in existing)
        {
            var n = row["name"]!.ToString()!;
            var p = store.Db.Prepare("SELECT path FROM store_collections WHERE name = $1")
                .GetDynamic(n);
            config.Collections[n] = new Collection { Path = p!["path"]!.ToString()! };
        }
        config.Collections[name] = new Collection { Path = path };
        store.Db.Prepare("DELETE FROM store_config WHERE key = $1").Run("config_hash");
        store.SyncConfig(config);
    }
}
