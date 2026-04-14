using FluentAssertions;
using Qmd.Core.Llm;

namespace Qmd.Core.Tests.Llm;

public class EmbeddingFormatterTests : IDisposable
{
    private readonly string? _originalEnv;

    public EmbeddingFormatterTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable("QMD_EMBED_MODEL");
        Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", _originalEnv);
    }

    [Fact]
    public void IsQwen3EmbeddingModel_MatchesQwenEmbed()
    {
        EmbeddingFormatter.IsQwen3EmbeddingModel("Qwen3-Embedding-0.6B").Should().BeTrue();
        EmbeddingFormatter.IsQwen3EmbeddingModel("hf:Qwen/Qwen3-Embedding-0.6B-GGUF/file.gguf").Should().BeTrue();
    }

    [Fact]
    public void IsQwen3EmbeddingModel_DoesNotMatchOtherModels()
    {
        EmbeddingFormatter.IsQwen3EmbeddingModel("embeddinggemma-300M").Should().BeFalse();
        EmbeddingFormatter.IsQwen3EmbeddingModel("nomic-embed-text").Should().BeFalse();
        EmbeddingFormatter.IsQwen3EmbeddingModel("Qwen3-Reranker").Should().BeFalse();
    }

    [Fact]
    public void FormatQueryForEmbedding_DefaultModel_TaskPrefix()
    {
        var result = EmbeddingFormatter.FormatQueryForEmbedding("test query");
        result.Should().Be("task: search result | query: test query");
    }

    [Fact]
    public void FormatQueryForEmbedding_Qwen3_InstructFormat()
    {
        var result = EmbeddingFormatter.FormatQueryForEmbedding("test query", "Qwen3-Embedding");
        result.Should().StartWith("Instruct:");
        result.Should().Contain("Query: test query");
    }

    [Fact]
    public void FormatDocForEmbedding_DefaultModel_WithTitle()
    {
        var result = EmbeddingFormatter.FormatDocForEmbedding("content", "My Title");
        result.Should().Be("title: My Title | text: content");
    }

    [Fact]
    public void FormatDocForEmbedding_DefaultModel_WithoutTitle()
    {
        var result = EmbeddingFormatter.FormatDocForEmbedding("content");
        result.Should().Be("title: none | text: content");
    }

    [Fact]
    public void FormatDocForEmbedding_Qwen3_WithTitle()
    {
        var result = EmbeddingFormatter.FormatDocForEmbedding("content", "My Title", "Qwen3-Embedding");
        result.Should().Be("My Title\ncontent");
    }

    [Fact]
    public void FormatDocForEmbedding_Qwen3_WithoutTitle()
    {
        var result = EmbeddingFormatter.FormatDocForEmbedding("content", null, "Qwen3-Embedding");
        result.Should().Be("content");
    }

    [Fact]
    public void LlmConstants_DefaultModels_AreHfUris()
    {
        LlmConstants.DefaultEmbedModel.Should().StartWith("hf:");
        LlmConstants.DefaultRerankModel.Should().StartWith("hf:");
        LlmConstants.DefaultGenerateModel.Should().StartWith("hf:");
    }

    [Fact]
    public void LlmConstants_CacheDir_UsesLocalAppData()
    {
        var dir = LlmConstants.GetModelCacheDir();
        dir.Should().Contain("qmd");
        dir.Should().Contain("models");
    }
}
