using FluentAssertions;
using Qmd.Core.Llm;

namespace Qmd.Core.Tests.Llm;

/// <summary>
/// Verifies that LlmConstants defaults match the hardcoded values.
/// </summary>
[Trait("Category", "Unit")]
public class LlmConstantsTests
{

    [Fact]
    public void DefaultEmbedModel_MatchesTypeScriptHardcoded()
    {
        LlmConstants.DefaultEmbedModel.Should().Be(
            "hf:ggml-org/embeddinggemma-300M-GGUF/embeddinggemma-300M-Q8_0.gguf");
    }

    [Fact]
    public void DefaultRerankModel_MatchesTypeScriptHardcoded()
    {
        LlmConstants.DefaultRerankModel.Should().Be(
            "hf:ggml-org/Qwen3-Reranker-0.6B-Q8_0-GGUF/qwen3-reranker-0.6b-q8_0.gguf");
    }

    [Fact]
    public void DefaultGenerateModel_MatchesTypeScriptHardcoded()
    {
        LlmConstants.DefaultGenerateModel.Should().Be(
            "hf:tobil/qmd-query-expansion-1.7B-gguf/qmd-query-expansion-1.7B-q4_k_m.gguf");
    }

    [Fact]
    public void EmbedContextSize_DefaultIs2048()
    {
        // Default is 2048 when no config or env variable overrides it
        LlmConstants.EmbedContextSize.Should().Be(2048);
    }

    [Fact]
    public void RerankContextSize_DefaultIs4096()
    {
        LlmConstants.RerankContextSize.Should().Be(4096);
    }

    [Fact]
    public void ModelResolution_EnvVarOverridesDefault()
    {
        var original = Environment.GetEnvironmentVariable("QMD_EMBED_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", "hf:custom/model/custom.gguf");
            var service = new LlamaSharpService(new LlamaSharpOptions());
            service.EmbedModelName.Should().Be("hf:custom/model/custom.gguf");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", original);
        }
    }

    [Fact]
    public void ModelResolution_ConfigOverridesEnvVar()
    {
        var original = Environment.GetEnvironmentVariable("QMD_EMBED_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", "hf:env/model/env.gguf");
            var service = new LlamaSharpService(new LlamaSharpOptions
            {
                EmbedModel = "hf:config/model/config.gguf"
            });
            service.EmbedModelName.Should().Be("hf:config/model/config.gguf");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", original);
        }
    }

    [Fact]
    public void ModelResolution_DefaultWhenNoEnvOrConfig()
    {
        // uses hardcoded default when no config or env is set
        var original = Environment.GetEnvironmentVariable("QMD_EMBED_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", null);
            var service = new LlamaSharpService(new LlamaSharpOptions());
            service.EmbedModelName.Should().Be(LlmConstants.DefaultEmbedModel);
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", original);
        }
    }

    [Fact]
    public void ExpandContextSize_UsesDefault_WhenNoEnvOrConfig()
    {
        var original = Environment.GetEnvironmentVariable("QMD_EXPAND_CONTEXT_SIZE");
        try
        {
            Environment.SetEnvironmentVariable("QMD_EXPAND_CONTEXT_SIZE", null);
            LlamaSharpService.ResolveExpandContextSize(null).Should().Be(2048);
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_EXPAND_CONTEXT_SIZE", original);
        }
    }

    [Fact]
    public void ExpandContextSize_EnvVarOverridesDefault()
    {
        var original = Environment.GetEnvironmentVariable("QMD_EXPAND_CONTEXT_SIZE");
        try
        {
            Environment.SetEnvironmentVariable("QMD_EXPAND_CONTEXT_SIZE", "4096");
            LlamaSharpService.ResolveExpandContextSize(null).Should().Be(4096);
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_EXPAND_CONTEXT_SIZE", original);
        }
    }

    [Fact]
    public void ExpandContextSize_ConfigOverridesEnvVar()
    {
        var original = Environment.GetEnvironmentVariable("QMD_EXPAND_CONTEXT_SIZE");
        try
        {
            Environment.SetEnvironmentVariable("QMD_EXPAND_CONTEXT_SIZE", "4096");
            LlamaSharpService.ResolveExpandContextSize(1024).Should().Be(1024);
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_EXPAND_CONTEXT_SIZE", original);
        }
    }

    [Fact]
    public void ExpandContextSize_InvalidEnvVar_FallsBackToDefault()
    {
        var original = Environment.GetEnvironmentVariable("QMD_EXPAND_CONTEXT_SIZE");
        try
        {
            Environment.SetEnvironmentVariable("QMD_EXPAND_CONTEXT_SIZE", "not-a-number");
            LlamaSharpService.ResolveExpandContextSize(null).Should().Be(2048);
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_EXPAND_CONTEXT_SIZE", original);
        }
    }

    [Fact]
    public void ExpandContextSize_InvalidConfigValue_Throws()
    {
        var act = () => LlamaSharpService.ResolveExpandContextSize(0);
        act.Should().Throw<ArgumentException>().WithMessage("*Invalid expandContextSize*");
    }

    [Fact]
    public void EmbeddingFormatter_EnvVarAffectsQueryFormatting()
    {
        var original = Environment.GetEnvironmentVariable("QMD_EMBED_MODEL");
        try
        {
            // Set to a Qwen3 embedding model — should switch to instruct format
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", "hf:org/qwen-embed-model/model.gguf");
            var formatted = EmbeddingFormatter.FormatQueryForEmbedding("test query");
            formatted.Should().Contain("Instruct:");

            // Set to default (non-Qwen3) — should use task prefix format
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", null);
            var defaultFormatted = EmbeddingFormatter.FormatQueryForEmbedding("test query");
            defaultFormatted.Should().Contain("task: search result");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", original);
        }
    }
}
