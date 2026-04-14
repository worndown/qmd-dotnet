using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Embedding;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Embedding;

[Trait("Category", "Database")]
public class EmbeddingOperationsTests : IDisposable
{
    private readonly IQmdDatabase _db;
    private readonly EmbeddingRepository _embeddingRepo;
    private readonly DocumentRepository _docRepo;

    public EmbeddingOperationsTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
        _embeddingRepo = new EmbeddingRepository(_db);
        _docRepo = new DocumentRepository(_db);
        SeedDocs();
    }

    public void Dispose() => _db.Dispose();

    private void SeedDocs()
    {
        _docRepo.InsertContent("hash1", "Content 1", "2025-01-01");
        _docRepo.InsertContent("hash2", "Content 2", "2025-01-01");
        _docRepo.InsertDocument("docs", "a.md", "Doc A", "hash1", "2025-01-01", "2025-01-01");
        _docRepo.InsertDocument("docs", "b.md", "Doc B", "hash2", "2025-01-01", "2025-01-01");
    }

    [Fact]
    public void GetPendingEmbeddingDocs_ReturnsDocsWithoutEmbeddings()
    {
        var pending = _embeddingRepo.GetPendingEmbeddingDocs();
        pending.Should().HaveCount(2);
        pending.Should().Contain(d => d.Hash == "hash1");
        pending.Should().Contain(d => d.Hash == "hash2");
    }

    [Fact]
    public void GetPendingEmbeddingDocs_SkipsInactiveDocs()
    {
        _docRepo.DeactivateDocument("docs", "b.md");
        var pending = _embeddingRepo.GetPendingEmbeddingDocs();
        pending.Should().HaveCount(1);
        pending[0].Hash.Should().Be("hash1");
    }

    [Fact]
    public void GetEmbeddingDocsForBatch_LoadsBodies()
    {
        var pending = _embeddingRepo.GetPendingEmbeddingDocs();
        var docs = _embeddingRepo.GetEmbeddingDocsForBatch(pending);
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

        _embeddingRepo.ClearAllEmbeddings();

        var count = _db.Prepare("SELECT COUNT(*) as cnt FROM content_vectors").GetDynamic();
        Convert.ToInt64(count!["cnt"]).Should().Be(0);
    }

    [Fact]
    public void FloatArrayToBytes_CorrectLength()
    {
        var floats = new float[] { 1.0f, 2.0f, 3.0f };
        var bytes = EmbeddingRepository.FloatArrayToBytes(floats);
        bytes.Length.Should().Be(12); // 3 * 4 bytes
    }

    [Fact]
    public void FloatArrayToBytes_Roundtrip()
    {
        var original = new float[] { 0.1f, -0.5f, 3.14f };
        var bytes = EmbeddingRepository.FloatArrayToBytes(original);
        var restored = new float[3];
        Buffer.BlockCopy(bytes, 0, restored, 0, bytes.Length);
        restored.Should().BeEquivalentTo(original);
    }
}
