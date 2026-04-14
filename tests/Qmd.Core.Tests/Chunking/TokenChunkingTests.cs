using FluentAssertions;
using Qmd.Core.Chunking;
using Qmd.Core.Models;

namespace Qmd.Core.Tests.Chunking;

public class TokenChunkingTests
{
    private readonly ITokenizer _charTokenizer = new CharBasedTokenizer();

    [Fact]
    public void ChunkDocumentByTokens_SingleChunkForSmallDocs()
    {
        var content = "This is a small document.";
        var chunks = DocumentChunker.ChunkDocumentByTokens(_charTokenizer, content, 900, 135);

        chunks.Should().HaveCount(1);
        chunks[0].Text.Should().Be(content);
        chunks[0].Pos.Should().Be(0);
        chunks[0].Tokens.Should().BeGreaterThan(0);
        chunks[0].Tokens.Should().BeLessThan(900);
    }

    [Fact]
    public void ChunkDocumentByTokens_SplitsLargeDocuments()
    {
        var content = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 250));
        var chunks = DocumentChunker.ChunkDocumentByTokens(_charTokenizer, content, 900, 135);

        chunks.Count.Should().BeGreaterThan(1);

        foreach (var chunk in chunks)
        {
            chunk.Tokens.Should().BeLessThanOrEqualTo(950); // Allow slight overage
            chunk.Tokens.Should().BeGreaterThan(0);
        }

        // Positions should be increasing
        for (int i = 1; i < chunks.Count; i++)
            chunks[i].Pos.Should().BeGreaterThan(chunks[i - 1].Pos);
    }

    [Fact]
    public void ChunkDocumentByTokens_CreatesOverlappingChunks()
    {
        var content = string.Concat(Enumerable.Repeat("Word ", 500));
        var chunks = DocumentChunker.ChunkDocumentByTokens(_charTokenizer, content, 200, 30);

        chunks.Count.Should().BeGreaterThan(1);

        // Consecutive chunks should overlap in position
        for (int i = 1; i < chunks.Count; i++)
        {
            var prevEnd = chunks[i - 1].Pos + chunks[i - 1].Text.Length;
            chunks[i].Pos.Should().BeLessThan(prevEnd);
        }
    }

    [Fact]
    public void ChunkDocumentByTokens_ReturnsActualTokenCounts()
    {
        var content = "Hello world, this is a test.";
        var chunks = DocumentChunker.ChunkDocumentByTokens(_charTokenizer, content);

        chunks.Should().HaveCount(1);
        chunks[0].Tokens.Should().BeGreaterThan(0);
        chunks[0].Tokens.Should().BeLessThan(content.Length); // Tokens < chars for English
    }

    /// <summary>
    /// Tokenizer that reports 1 token per character, forcing re-splitting
    /// since the initial 3 chars/token estimate will undercount tokens by 3x.
    /// </summary>
    private class OneCharPerTokenTokenizer : ITokenizer
    {
        public int CountTokens(string text) => text.Length;
    }

    [Fact]
    public void ChunkDocumentByTokens_ReSplitProducesValidChunks()
    {
        var content = new string('A', 5000); // 5000 chars = 5000 tokens with this tokenizer
        var chunks = DocumentChunker.ChunkDocumentByTokens(
            new OneCharPerTokenTokenizer(), content, 900, 135);

        chunks.Count.Should().BeGreaterThan(1);

        // Each chunk's reported token count should equal text length
        foreach (var chunk in chunks)
            chunk.Tokens.Should().Be(chunk.Text.Length);
    }

    [Fact]
    public void ChunkDocumentByTokens_ReSplitPositionsAreGloballyCorrect()
    {
        var content = string.Concat(Enumerable.Repeat("Word ", 2000)); // 10000 chars
        var chunks = DocumentChunker.ChunkDocumentByTokens(
            new OneCharPerTokenTokenizer(), content, 900, 0);

        // Positions should be non-decreasing
        for (int i = 1; i < chunks.Count; i++)
            chunks[i].Pos.Should().BeGreaterThanOrEqualTo(chunks[i - 1].Pos);

        // Text at each position should match the original content
        foreach (var chunk in chunks)
            content.Substring(chunk.Pos, chunk.Text.Length).Should().Be(chunk.Text);
    }

    [Fact]
    public void ChunkDocumentByTokens_RespectsCancellationToken()
    {
        var content = new string('A', 10000);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => DocumentChunker.ChunkDocumentByTokens(
            new OneCharPerTokenTokenizer(), content, 900, 0,
            cancellationToken: cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void ChunkDocumentByTokens_ReSplitWithOverlapProducesOverlappingChunks()
    {
        var content = new string('B', 5000);
        var chunks = DocumentChunker.ChunkDocumentByTokens(
            new OneCharPerTokenTokenizer(), content, 500, 75);

        chunks.Count.Should().BeGreaterThan(2);

        // Consecutive chunks should overlap
        for (int i = 1; i < chunks.Count; i++)
        {
            var prevEnd = chunks[i - 1].Pos + chunks[i - 1].Text.Length;
            chunks[i].Pos.Should().BeLessThan(prevEnd);
        }
    }
}
