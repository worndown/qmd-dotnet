using FluentAssertions;
using Qmd.Core.Embedding;
using Qmd.Core.Models;

namespace Qmd.Core.Tests.Embedding;

public class BatchAssemblerTests
{
    [Fact]
    public void BuildBatches_SingleDoc_OneBatch()
    {
        var docs = new List<PendingEmbeddingDoc> { new("h1", "a.md", 100) };
        var batches = BatchAssembler.BuildBatches(docs);
        batches.Should().HaveCount(1);
        batches[0].Should().HaveCount(1);
    }

    [Fact]
    public void BuildBatches_ExceedsMaxDocs_Splits()
    {
        var docs = Enumerable.Range(0, 5).Select(i => new PendingEmbeddingDoc($"h{i}", $"{i}.md", 10)).ToList();
        var batches = BatchAssembler.BuildBatches(docs, maxDocsPerBatch: 2);
        batches.Should().HaveCount(3); // 2, 2, 1
        batches[0].Should().HaveCount(2);
        batches[1].Should().HaveCount(2);
        batches[2].Should().HaveCount(1);
    }

    [Fact]
    public void BuildBatches_ExceedsMaxBytes_Splits()
    {
        var docs = new List<PendingEmbeddingDoc>
        {
            new("h1", "a.md", 500),
            new("h2", "b.md", 500),
            new("h3", "c.md", 500),
        };
        var batches = BatchAssembler.BuildBatches(docs, maxDocsPerBatch: 100, maxBatchBytes: 1100);
        batches.Should().HaveCount(2); // [500, 500] then [500] (third would exceed 1100)
    }

    [Fact]
    public void BuildBatches_EmptyInput_EmptyOutput()
    {
        BatchAssembler.BuildBatches([]).Should().BeEmpty();
    }

    [Fact]
    public void BuildBatches_LargeDocGetsOwnBatch()
    {
        var docs = new List<PendingEmbeddingDoc>
        {
            new("h1", "small.md", 10),
            new("h2", "huge.md", 10000),
            new("h3", "small2.md", 10),
        };
        var batches = BatchAssembler.BuildBatches(docs, maxDocsPerBatch: 100, maxBatchBytes: 100);
        batches.Should().HaveCount(3);
    }

    [Fact]
    public void BuildBatches_ExactlyAtLimit_SingleBatch()
    {
        var docs = Enumerable.Range(0, 3).Select(i => new PendingEmbeddingDoc($"h{i}", $"{i}.md", 100)).ToList();
        var batches = BatchAssembler.BuildBatches(docs, maxDocsPerBatch: 3, maxBatchBytes: 300);
        batches.Should().HaveCount(1);
    }
}
