using FluentAssertions;
using Qmd.Core.Llm;

namespace Qmd.Core.Tests.Llm;

/// <summary>
/// Tests for HuggingFace URI parsing (no network needed).
/// </summary>
public class ModelResolverTests
{
    [Fact]
    public void ParseHfUri_ValidUri()
    {
        var hfRef = ModelResolver.ParseHfUri("hf:ggml-org/embeddinggemma-300M-GGUF/embeddinggemma-300M-Q8_0.gguf");
        hfRef.Should().NotBeNull();
        hfRef!.Repo.Should().Be("ggml-org/embeddinggemma-300M-GGUF");
        hfRef.File.Should().Be("embeddinggemma-300M-Q8_0.gguf");
    }

    [Fact]
    public void ParseHfUri_ValidUri_WithSubpath()
    {
        var hfRef = ModelResolver.ParseHfUri("hf:user/repo/subdir/file.gguf");
        hfRef.Should().NotBeNull();
        hfRef!.Repo.Should().Be("user/repo");
        hfRef.File.Should().Be("subdir/file.gguf");
    }

    [Fact]
    public void ParseHfUri_InvalidUri_NotHf()
    {
        ModelResolver.ParseHfUri("/local/path/model.gguf").Should().BeNull();
        ModelResolver.ParseHfUri("C:/models/file.gguf").Should().BeNull();
    }

    [Fact]
    public void ParseHfUri_InvalidUri_TooFewParts()
    {
        ModelResolver.ParseHfUri("hf:repo").Should().BeNull();
        ModelResolver.ParseHfUri("hf:user/repo").Should().BeNull();
    }
}
