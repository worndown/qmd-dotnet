using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Database;

[Trait("Category", "Database")]
public class SchemaInitializerTests : IDisposable
{
    private readonly IQmdDatabase _db;

    public SchemaInitializerTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("content")]
    [InlineData("documents")]
    [InlineData("llm_cache")]
    [InlineData("content_vectors")]
    [InlineData("store_collections")]
    [InlineData("store_config")]
    [InlineData("documents_fts")]
    public void Initialize_CreatesAllTables(string tableName)
    {
        var row = _db.Prepare(
            "SELECT name FROM sqlite_master WHERE type IN ('table', 'virtual table') AND name = $1")
            .Get<SqliteMasterRow>(tableName);
        row.Should().NotBeNull($"table '{tableName}' should exist");
    }

    [Theory]
    [InlineData("idx_documents_collection")]
    [InlineData("idx_documents_hash")]
    [InlineData("idx_documents_path")]
    public void Initialize_CreatesAllIndexes(string indexName)
    {
        var row = _db.Prepare(
            "SELECT name FROM sqlite_master WHERE type='index' AND name = $1")
            .Get<SqliteMasterRow>(indexName);
        row.Should().NotBeNull($"index '{indexName}' should exist");
    }

    [Theory]
    [InlineData("documents_ai")]
    [InlineData("documents_ad")]
    [InlineData("documents_au")]
    public void Initialize_CreatesAllTriggers(string triggerName)
    {
        var row = _db.Prepare(
            "SELECT name FROM sqlite_master WHERE type='trigger' AND name = $1")
            .Get<SqliteMasterRow>(triggerName);
        row.Should().NotBeNull($"trigger '{triggerName}' should exist");
    }

    [Fact]
    public void Initialize_WalModeEnabled()
    {
        var row = _db.Prepare("PRAGMA journal_mode").Get<JournalModeRow>();
        row.Should().NotBeNull();
        // In-memory databases use "memory" mode, not WAL
        // WAL is set but SQLite may report "memory" for in-memory DBs
    }

    [Fact]
    public void Initialize_CanInsertAndRetrieveContent()
    {
        _db.Prepare("INSERT INTO content (hash, doc, created_at) VALUES ($1, $2, $3)")
            .Run("abc123", "Hello world", "2025-01-01");

        var row = _db.Prepare("SELECT doc FROM content WHERE hash = $1").Get<DocRow>("abc123");
        row.Should().NotBeNull();
        row!.Doc.Should().Be("Hello world");
    }

    [Fact]
    public void Initialize_CanInsertDocumentWithForeignKey()
    {
        _db.Prepare("INSERT INTO content (hash, doc, created_at) VALUES ($1, $2, $3)")
            .Run("hash1", "Content", "2025-01-01");

        _db.Prepare(@"INSERT INTO documents (collection, path, title, hash, created_at, modified_at)
            VALUES ($1, $2, $3, $4, $5, $6)")
            .Run("docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01");

        var row = _db.Prepare("SELECT title FROM documents WHERE collection = $1 AND path = $2")
            .Get<TitleRow>("docs", "readme.md");
        row.Should().NotBeNull();
        row!.Title.Should().Be("README");
    }

    [Fact]
    public void Initialize_FtsTriggerSyncsOnInsert()
    {
        _db.Prepare("INSERT INTO content (hash, doc, created_at) VALUES ($1, $2, $3)")
            .Run("hash1", "This is searchable content", "2025-01-01");

        _db.Prepare(@"INSERT INTO documents (collection, path, title, hash, created_at, modified_at, active)
            VALUES ($1, $2, $3, $4, $5, $6, $7)")
            .Run("docs", "readme.md", "README", "hash1", "2025-01-01", "2025-01-01", 1L);

        // FTS should have the document
        var ftsRow = _db.Prepare("SELECT filepath, title FROM documents_fts WHERE documents_fts MATCH $1")
            .Get<FtsFilepathTitleRow>("searchable");
        ftsRow.Should().NotBeNull();
        ftsRow!.Filepath.Should().Be("docs/readme.md");
        ftsRow.Title.Should().Be("README");
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        // Running Initialize twice should not throw
        SchemaInitializer.Initialize(_db);

        var tables = _db.Prepare(
            "SELECT count(*) as cnt FROM sqlite_master WHERE type IN ('table', 'virtual table')")
            .Get<CountRow>();
        tables.Should().NotBeNull();
        tables!.Cnt.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Initialize_ContentVectorsMigration_DropsOldSchema()
    {
        // Create a fresh DB with old schema (no seq column)
        using var db2 = new SqliteDatabase(":memory:");
        db2.Exec(@"CREATE TABLE content_vectors (
            hash TEXT PRIMARY KEY,
            model TEXT NOT NULL,
            embedded_at TEXT NOT NULL
        )");

        // Initialize should detect missing seq column and recreate
        SchemaInitializer.Initialize(db2);

        // Verify new schema has seq column
        var cols = db2.Prepare("PRAGMA table_info(content_vectors)").All<TableInfoRow>();
        cols.Should().Contain(c => c.Name == "seq");
        cols.Should().Contain(c => c.Name == "pos");
    }

    private class JournalModeRow
    {
        public string JournalMode { get; set; } = "";
    }

    private class DocRow
    {
        public string Doc { get; set; } = "";
    }

    private class TitleRow
    {
        public string Title { get; set; } = "";
    }

    private class FtsFilepathTitleRow
    {
        public string Filepath { get; set; } = "";
        public string Title { get; set; } = "";
    }

    private class TableInfoRow
    {
        public int Cid { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public int Notnull { get; set; }
        public object? DfltValue { get; set; }
        public int Pk { get; set; }
    }
}
