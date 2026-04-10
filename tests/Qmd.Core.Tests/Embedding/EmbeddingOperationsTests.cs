using FluentAssertions;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Embedding;

namespace Qmd.Core.Tests.Embedding;

public class EmbeddingOperationsTests : IDisposable
{
    private readonly SqliteDatabase _db;

    public EmbeddingOperationsTests()
    {
        _db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(_db);
        SeedDocs();
    }

    public void Dispose() => _db.Dispose();

    private void SeedDocs()
    {
        ContentHasher.InsertContent(_db, "hash1", "Content 1", "2025-01-01");
        ContentHasher.InsertContent(_db, "hash2", "Content 2", "2025-01-01");
        DocumentOperations.InsertDocument(_db, "docs", "a.md", "Doc A", "hash1", "2025-01-01", "2025-01-01");
        DocumentOperations.InsertDocument(_db, "docs", "b.md", "Doc B", "hash2", "2025-01-01", "2025-01-01");
    }

    [Fact]
    public void GetPendingEmbeddingDocs_ReturnsDocsWithoutEmbeddings()
    {
        var pending = EmbeddingOperations.GetPendingEmbeddingDocs(_db);
        pending.Should().HaveCount(2);
        pending.Should().Contain(d => d.Hash == "hash1");
        pending.Should().Contain(d => d.Hash == "hash2");
    }

    [Fact]
    public void GetPendingEmbeddingDocs_SkipsInactiveDocs()
    {
        DocumentOperations.DeactivateDocument(_db, "docs", "b.md");
        var pending = EmbeddingOperations.GetPendingEmbeddingDocs(_db);
        pending.Should().HaveCount(1);
        pending[0].Hash.Should().Be("hash1");
    }

    [Fact]
    public void GetEmbeddingDocsForBatch_LoadsBodies()
    {
        var pending = EmbeddingOperations.GetPendingEmbeddingDocs(_db);
        var docs = EmbeddingOperations.GetEmbeddingDocsForBatch(_db, pending);
        docs.Should().HaveCount(2);
        docs.Should().Contain(d => d.Body == "Content 1");
        docs.Should().Contain(d => d.Body == "Content 2");
    }

    [Fact]
    public void ClearAllEmbeddings_RemovesContentVectors()
    {
        // Insert a content_vectors row first
        _db.Prepare("INSERT INTO content_vectors (hash, seq, pos, model, embedded_at) VALUES ($1, $2, $3, $4, $5)")
            .Run("hash1", 0L, 0L, "test-model", "2025-01-01");

        EmbeddingOperations.ClearAllEmbeddings(_db);

        var count = _db.Prepare("SELECT COUNT(*) as cnt FROM content_vectors").GetDynamic();
        Convert.ToInt64(count!["cnt"]).Should().Be(0);
    }

    [Fact]
    public void FloatArrayToBytes_CorrectLength()
    {
        var floats = new float[] { 1.0f, 2.0f, 3.0f };
        var bytes = EmbeddingOperations.FloatArrayToBytes(floats);
        bytes.Length.Should().Be(12); // 3 * 4 bytes
    }

    [Fact]
    public void FloatArrayToBytes_Roundtrip()
    {
        var original = new float[] { 0.1f, -0.5f, 3.14f };
        var bytes = EmbeddingOperations.FloatArrayToBytes(original);
        var restored = new float[3];
        Buffer.BlockCopy(bytes, 0, restored, 0, bytes.Length);
        restored.Should().BeEquivalentTo(original);
    }
}
