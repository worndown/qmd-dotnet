using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Embedding;
using Qmd.Core.Models;
using Qmd.Core.Tests.Llm;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Embedding;

[Trait("Category", "Database")]
public class EmbeddingPipelineTests : IDisposable
{
    private readonly IQmdDatabase _db;
    private readonly MockLlmService _llm;
    private readonly EmbeddingRepository _embeddingRepo;
    private readonly DocumentRepository _docRepo;

    public EmbeddingPipelineTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
        _llm = new MockLlmService();
        _embeddingRepo = new EmbeddingRepository(_db);
        _docRepo = new DocumentRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private void SeedDoc(string path, string content)
    {
        var hash = Qmd.Core.Content.ContentHasher.HashContent(content);
        _docRepo.InsertContent(hash, content, "2025-01-01");
        _docRepo.InsertDocument("docs", path, path, hash, "2025-01-01", "2025-01-01");
    }

    [Fact]
    public async Task GenerateEmbeddings_NoPendingDocs_ReturnsZero()
    {
        var result = await EmbeddingPipeline.GenerateEmbeddingsAsync(_db, _llm, _embeddingRepo);
        result.DocsProcessed.Should().Be(0);
        result.ChunksEmbedded.Should().Be(0);
    }

    [Fact]
    public async Task GenerateEmbeddings_ProcessesSingleDoc()
    {
        SeedDoc("small.md", "Short content for embedding");
        // Ensure vec table exists before pipeline
        VecExtension.ResetForTesting();

        var result = await EmbeddingPipeline.GenerateEmbeddingsAsync(_db, _llm, _embeddingRepo,
            ensureVecTable: dims => {
                try { VecExtension.EnsureVecTable(_db, dims); } catch { /* vec not loaded */ }
            });

        result.DocsProcessed.Should().Be(1);
        result.ChunksEmbedded.Should().BeGreaterThanOrEqualTo(1);
        _llm.EmbedBatchCallCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GenerateEmbeddings_InsertsContentVectors()
    {
        SeedDoc("test.md", "Content to embed");

        await EmbeddingPipeline.GenerateEmbeddingsAsync(_db, _llm, _embeddingRepo,
            ensureVecTable: _ => { /* skip vec table for non-vec tests */ });

        // Verify content_vectors was populated
        var cvCount = _db.Prepare("SELECT COUNT(*) as cnt FROM content_vectors").Get<CountRow>();
        cvCount!.Cnt.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GenerateEmbeddings_ForceMode_ClearsExisting()
    {
        SeedDoc("test.md", "Content");

        // First run
        await EmbeddingPipeline.GenerateEmbeddingsAsync(_db, _llm, _embeddingRepo,
            ensureVecTable: _ => { });

        // Second run with force
        var result = await EmbeddingPipeline.GenerateEmbeddingsAsync(_db, _llm, _embeddingRepo,
            new EmbedPipelineOptions { Force = true },
            ensureVecTable: _ => { });

        result.DocsProcessed.Should().Be(1); // Re-processed
    }

    [Fact]
    public async Task GenerateEmbeddings_SkipsAlreadyEmbedded()
    {
        SeedDoc("test.md", "Content");

        // First run
        await EmbeddingPipeline.GenerateEmbeddingsAsync(_db, _llm, _embeddingRepo,
            ensureVecTable: _ => { });

        // Second run without force
        var result = await EmbeddingPipeline.GenerateEmbeddingsAsync(_db, _llm, _embeddingRepo,
            ensureVecTable: _ => { });

        result.DocsProcessed.Should().Be(0); // Already embedded
    }

    [Fact]
    public async Task GenerateEmbeddings_ReportsProgress()
    {
        SeedDoc("a.md", "Content A");
        SeedDoc("b.md", "Content B");
        var progressReports = new List<EmbedProgress>();

        await EmbeddingPipeline.GenerateEmbeddingsAsync(_db, _llm, _embeddingRepo,
            new EmbedPipelineOptions { Progress = new TestHelpers.SyncProgress<EmbedProgress>(p => progressReports.Add(p)) },
            ensureVecTable: _ => { });

        progressReports.Should().NotBeEmpty();
        progressReports.Last().ChunksEmbedded.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GenerateEmbeddings_RespectsBatchLimits()
    {
        // Add 5 docs with small content
        for (int i = 0; i < 5; i++)
            SeedDoc($"{i}.md", $"Content {i}");

        var result = await EmbeddingPipeline.GenerateEmbeddingsAsync(_db, _llm, _embeddingRepo,
            new EmbedPipelineOptions { MaxDocsPerBatch = 2 },
            ensureVecTable: _ => { });

        result.DocsProcessed.Should().Be(5);
    }

    [Fact]
    public async Task GenerateEmbeddings_FlushesOnMaxBatchBytes()
    {
        var docOne = "# One\n\n" + new string('A', 36);
        var docTwo = "# Two\n\n" + new string('B', 36);
        var docThree = "# Three\n\n" + new string('C', 36);
        var batchLimit = System.Text.Encoding.UTF8.GetByteCount(docOne)
            + System.Text.Encoding.UTF8.GetByteCount(docTwo)
            + 1;

        SeedDoc("a-one.md", docOne);
        SeedDoc("b-two.md", docTwo);
        SeedDoc("c-three.md", docThree);

        var result = await EmbeddingPipeline.GenerateEmbeddingsAsync(_db, _llm, _embeddingRepo,
            new EmbedPipelineOptions { MaxDocsPerBatch = 64, MaxBatchBytes = batchLimit },
            ensureVecTable: _ => { });

        // With batchLimit just above docOne+docTwo, first batch fits 2 docs, second gets 1
        _llm.EmbedBatchCallCount.Should().BeGreaterThanOrEqualTo(2);
        result.DocsProcessed.Should().Be(3);
        result.ChunksEmbedded.Should().Be(3);
    }

    [Fact]
    public async Task GenerateEmbeddings_PassesModelThroughToEmbedCallsAndMetadata()
    {
        var model = "hf:Qwen/Qwen3-Embedding-0.6B-GGUF/Qwen3-Embedding-0.6B-Q8_0.gguf";

        SeedDoc("one.md", "# One\n\nAlpha");

        var result = await EmbeddingPipeline.GenerateEmbeddingsAsync(_db, _llm, _embeddingRepo,
            new EmbedPipelineOptions { Model = model },
            ensureVecTable: _ => { });

        result.ChunksEmbedded.Should().Be(1);

        // Verify model was passed through to embed batch calls
        _llm.EmbedBatchOptionsCalls.Should().NotBeEmpty();
        _llm.EmbedBatchOptionsCalls[0]?.Model.Should().Be(model);

        // Verify model stored in content_vectors table
        var row = _db.Prepare("SELECT DISTINCT model FROM content_vectors").Get<ModelRow>();
        row.Should().NotBeNull();
        row!.Model.Should().Be(model);
    }

    [Fact]
    public async Task GenerateEmbeddings_RejectsInvalidBatchLimits()
    {
        var act1 = () => EmbeddingPipeline.GenerateEmbeddingsAsync(_db, _llm, _embeddingRepo,
            new EmbedPipelineOptions { MaxDocsPerBatch = 0 },
            ensureVecTable: _ => { });
        await act1.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*maxDocsPerBatch*");

        var act2 = () => EmbeddingPipeline.GenerateEmbeddingsAsync(_db, _llm, _embeddingRepo,
            new EmbedPipelineOptions { MaxBatchBytes = 0 },
            ensureVecTable: _ => { });
        await act2.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*maxBatchBytes*");
    }

    private class ModelRow
    {
        public string Model { get; set; } = "";
    }
}
