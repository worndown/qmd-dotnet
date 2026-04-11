namespace Qmd.Core.Database;

/// <summary>
/// Initializes the QMD SQLite database schema.
/// </summary>
internal static class SchemaInitializer
{
    public static void Initialize(IQmdDatabase db)
    {
        db.Exec("PRAGMA journal_mode = WAL");
        db.Exec("PRAGMA foreign_keys = ON");

        // Drop legacy tables
        db.Exec("DROP TABLE IF EXISTS path_contexts");
        db.Exec("DROP TABLE IF EXISTS collections");

        // Content-addressable storage
        db.Exec(@"
            CREATE TABLE IF NOT EXISTS content (
                hash TEXT PRIMARY KEY,
                doc TEXT NOT NULL,
                created_at TEXT NOT NULL
            )
        ");

        // Documents table
        db.Exec(@"
            CREATE TABLE IF NOT EXISTS documents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                collection TEXT NOT NULL,
                path TEXT NOT NULL,
                title TEXT NOT NULL,
                hash TEXT NOT NULL,
                created_at TEXT NOT NULL,
                modified_at TEXT NOT NULL,
                active INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (hash) REFERENCES content(hash) ON DELETE CASCADE,
                UNIQUE(collection, path)
            )
        ");

        db.Exec("CREATE INDEX IF NOT EXISTS idx_documents_collection ON documents(collection, active)");
        db.Exec("CREATE INDEX IF NOT EXISTS idx_documents_hash ON documents(hash)");
        db.Exec("CREATE INDEX IF NOT EXISTS idx_documents_path ON documents(path, active)");

        // LLM cache
        db.Exec(@"
            CREATE TABLE IF NOT EXISTS llm_cache (
                hash TEXT PRIMARY KEY,
                result TEXT NOT NULL,
                created_at TEXT NOT NULL
            )
        ");

        // Migration: check content_vectors for seq column
        var cvInfo = db.Prepare("PRAGMA table_info(content_vectors)").All<ColumnInfo>();
        var hasSeqColumn = cvInfo.Any(c => c.Name == "seq");
        if (cvInfo.Count > 0 && !hasSeqColumn)
        {
            db.Exec("DROP TABLE IF EXISTS content_vectors");
            db.Exec("DROP TABLE IF EXISTS vectors_vec");
        }

        // Content vectors
        db.Exec(@"
            CREATE TABLE IF NOT EXISTS content_vectors (
                hash TEXT NOT NULL,
                seq INTEGER NOT NULL DEFAULT 0,
                pos INTEGER NOT NULL DEFAULT 0,
                model TEXT NOT NULL,
                embedded_at TEXT NOT NULL,
                PRIMARY KEY (hash, seq)
            )
        ");

        // Store collections
        db.Exec(@"
            CREATE TABLE IF NOT EXISTS store_collections (
                name TEXT PRIMARY KEY,
                path TEXT NOT NULL,
                pattern TEXT NOT NULL DEFAULT '**/*.md',
                ignore_patterns TEXT,
                include_by_default INTEGER DEFAULT 1,
                update_command TEXT,
                context TEXT
            )
        ");

        // Store config (key-value)
        db.Exec(@"
            CREATE TABLE IF NOT EXISTS store_config (
                key TEXT PRIMARY KEY,
                value TEXT
            )
        ");

        // FTS5 full-text search
        db.Exec(@"
            CREATE VIRTUAL TABLE IF NOT EXISTS documents_fts USING fts5(
                filepath, title, body,
                tokenize='porter unicode61'
            )
        ");

        // Triggers to keep FTS in sync
        db.Exec(@"
            CREATE TRIGGER IF NOT EXISTS documents_ai AFTER INSERT ON documents
            WHEN new.active = 1
            BEGIN
                INSERT INTO documents_fts(rowid, filepath, title, body)
                SELECT
                    new.id,
                    new.collection || '/' || new.path,
                    new.title,
                    (SELECT doc FROM content WHERE hash = new.hash)
                WHERE new.active = 1;
            END
        ");

        db.Exec(@"
            CREATE TRIGGER IF NOT EXISTS documents_ad AFTER DELETE ON documents BEGIN
                DELETE FROM documents_fts WHERE rowid = old.id;
            END
        ");

        db.Exec(@"
            CREATE TRIGGER IF NOT EXISTS documents_au AFTER UPDATE ON documents
            BEGIN
                DELETE FROM documents_fts WHERE rowid = old.id AND new.active = 0;

                INSERT OR REPLACE INTO documents_fts(rowid, filepath, title, body)
                SELECT
                    new.id,
                    new.collection || '/' || new.path,
                    new.title,
                    (SELECT doc FROM content WHERE hash = new.hash)
                WHERE new.active = 1;
            END
        ");
    }

    private class ColumnInfo
    {
        public string Name { get; set; } = "";
    }
}
