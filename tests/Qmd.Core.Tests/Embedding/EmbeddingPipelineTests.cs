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
    private readonly IQmdDatabase db;
    private readonly MockLlmService llm;
    private readonly EmbeddingRepository embeddingRepo;
    private readonly DocumentRepository docRepo;

    public EmbeddingPipelineTests()
    {
        this.db = TestDbHelper.CreateInMemoryDb();
        this.llm = new MockLlmService();
        this.embeddingRepo = new EmbeddingRepository(this.db);
        this.docRepo = new DocumentRepository(this.db);
    }

    public void Dispose() => this.db.Dispose();

    private void SeedDoc(string path, string content)
    {
        var hash = Qmd.Core.Content.ContentHasher.HashContent(content);
        this.docRepo.InsertContent(hash, content, "2025-01-01");
        this.docRepo.InsertDocument("docs", path, path, hash, "2025-01-01", "2025-01-01");
    }

    [Fact]
    public async Task GenerateEmbeddings_NoPendingDocs_ReturnsZero()
    {
        var result = await EmbeddingPipeline.GenerateEmbeddingsAsync(this.db, this.llm, this.embeddingRepo);
        result.DocsProcessed.Should().Be(0);
        result.ChunksEmbedded.Should().Be(0);
    }

    [Fact]
    public async Task GenerateEmbeddings_ProcessesSingleDoc()
    {
        this.SeedDoc("small.md", "Short content for embedding");
        // Ensure vec table exists before pipeline
        VecExtension.ResetForTesting();

        var result = await EmbeddingPipeline.GenerateEmbeddingsAsync(this.db, this.llm, this.embeddingRepo,
            ensureVecTable: dims => {
                try { VecExtension.EnsureVecTable(this.db, dims); } catch { /* vec not loaded */ }
            });

        result.DocsProcessed.Should().Be(1);
        result.ChunksEmbedded.Should().BeGreaterThanOrEqualTo(1);
        this.llm.EmbedBatchCallCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GenerateEmbeddings_InsertsContentVectors()
    {
        this.SeedDoc("test.md", "Content to embed");

        await EmbeddingPipeline.GenerateEmbeddingsAsync(this.db, this.llm, this.embeddingRepo,
            ensureVecTable: _ => { /* skip vec table for non-vec tests */ });

        // Verify content_vectors was populated
        var cvCount = this.db.Prepare("SELECT COUNT(*) as cnt FROM content_vectors").Get<CountRow>();
        cvCount!.Cnt.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GenerateEmbeddings_ForceMode_ClearsExisting()
    {
        this.SeedDoc("test.md", "Content");

        // First run
        await EmbeddingPipeline.GenerateEmbeddingsAsync(this.db, this.llm, this.embeddingRepo,
            ensureVecTable: _ => { });

        // Second run with force
        var result = await EmbeddingPipeline.GenerateEmbeddingsAsync(this.db, this.llm, this.embeddingRepo,
            new EmbedPipelineOptions { Force = true },
            ensureVecTable: _ => { });

        result.DocsProcessed.Should().Be(1); // Re-processed
    }

    [Fact]
    public async Task GenerateEmbeddings_SkipsAlreadyEmbedded()
    {
        this.SeedDoc("test.md", "Content");

        // First run
        await EmbeddingPipeline.GenerateEmbeddingsAsync(this.db, this.llm, this.embeddingRepo,
            ensureVecTable: _ => { });

        // Second run without force
        var result = await EmbeddingPipeline.GenerateEmbeddingsAsync(this.db, this.llm, this.embeddingRepo,
            ensureVecTable: _ => { });

        result.DocsProcessed.Should().Be(0); // Already embedded
    }

    [Fact]
    public async Task GenerateEmbeddings_ReportsProgress()
    {
        this.SeedDoc("a.md", "Content A");
        this.SeedDoc("b.md", "Content B");
        var progressReports = new List<EmbedProgress>();

        await EmbeddingPipeline.GenerateEmbeddingsAsync(this.db, this.llm, this.embeddingRepo,
            new EmbedPipelineOptions { Progress = new TestHelpers.SyncProgress<EmbedProgress>(p => progressReports.Add(p)) },
            ensureVecTable: _ => { });

        progressReports.Should().NotBeEmpty();
        progressReports.Last().ChunksEmbedded.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GenerateEmbeddings_RespectsBatchLimits()
    {
        // Add 5 docs with small content
        for (int i = 0; i < 5; i++) this.SeedDoc($"{i}.md", $"Content {i}");

        var result = await EmbeddingPipeline.GenerateEmbeddingsAsync(this.db, this.llm, this.embeddingRepo,
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

        this.SeedDoc("a-one.md", docOne);
        this.SeedDoc("b-two.md", docTwo);
        this.SeedDoc("c-three.md", docThree);

        var result = await EmbeddingPipeline.GenerateEmbeddingsAsync(this.db, this.llm, this.embeddingRepo,
            new EmbedPipelineOptions { MaxDocsPerBatch = 64, MaxBatchBytes = batchLimit },
            ensureVecTable: _ => { });

        // With batchLimit just above docOne+docTwo, first batch fits 2 docs, second gets 1
        this.llm.EmbedBatchCallCount.Should().BeGreaterThanOrEqualTo(2);
        result.DocsProcessed.Should().Be(3);
        result.ChunksEmbedded.Should().Be(3);
    }

    [Fact]
    public async Task GenerateEmbeddings_PassesModelThroughToEmbedCallsAndMetadata()
    {
        var model = "hf:Qwen/Qwen3-Embedding-0.6B-GGUF/Qwen3-Embedding-0.6B-Q8_0.gguf";

        this.SeedDoc("one.md", "# One\n\nAlpha");

        var result = await EmbeddingPipeline.GenerateEmbeddingsAsync(this.db, this.llm, this.embeddingRepo,
            new EmbedPipelineOptions { Model = model },
            ensureVecTable: _ => { });

        result.ChunksEmbedded.Should().Be(1);

        // Verify model was passed through to embed batch calls
        this.llm.EmbedBatchOptionsCalls.Should().NotBeEmpty();
        this.llm.EmbedBatchOptionsCalls[0]?.Model.Should().Be(model);

        // Verify model stored in content_vectors table
        var row = this.db.Prepare("SELECT DISTINCT model FROM content_vectors").Get<ModelRow>();
        row.Should().NotBeNull();
        row!.Model.Should().Be(model);
    }

    [Fact]
    public async Task GenerateEmbeddings_RejectsInvalidBatchLimits()
    {
        var act1 = () => EmbeddingPipeline.GenerateEmbeddingsAsync(this.db, this.llm, this.embeddingRepo,
            new EmbedPipelineOptions { MaxDocsPerBatch = 0 },
            ensureVecTable: _ => { });
        await act1.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*maxDocsPerBatch*");

        var act2 = () => EmbeddingPipeline.GenerateEmbeddingsAsync(this.db, this.llm, this.embeddingRepo,
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
