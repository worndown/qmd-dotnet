using FluentAssertions;
using Qmd.Core.Llm;

namespace Qmd.Core.Tests.Llm;

/// <summary>
/// Verifies that LlmConstants defaults match the hardcoded values.
/// </summary>
[Trait("Category", "Unit")]
[Collection("LlmEnvironment")]
public class LlmConstantsTests
{

    [Fact]
    public void DefaultEmbedModel_MatchesTypeScriptHardcoded()
    {
        LlmConstants.DefaultEmbedModel.Should().Be(
            "hf:worndown/Qwen3-Embedding-0.6B-GGUF/Qwen3-Embedding-0.6B-f16.gguf");
    }

    [Fact]
    public void DefaultRerankModel_MatchesTypeScriptHardcoded()
    {
        LlmConstants.DefaultRerankModel.Should().Be(
            "hf:worndown/Qwen3-Reranker-0.6B-GGUF/Qwen3-Reranker-0.6B-f16.gguf");
    }

    [Fact]
    public void DefaultGenerateModel_MatchesTypeScriptHardcoded()
    {
        LlmConstants.DefaultGenerateModel.Should().Be(
            "hf:tobil/qmd-query-expansion-1.7B-gguf/qmd-query-expansion-1.7B-q8_0.gguf");
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
    public void ResolveEmbedModel_ReturnsDefault_WhenNoOverride()
    {
        var original = Environment.GetEnvironmentVariable("QMD_EMBED_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", null);
            LlmServiceFactory.ResolveEmbedModel().Should().Be(LlmConstants.DefaultEmbedModel);
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", original);
        }
    }

    [Fact]
    public void ResolveEmbedModel_EnvVarOverridesDefault()
    {
        var original = Environment.GetEnvironmentVariable("QMD_EMBED_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", "hf:custom/embed/model.gguf");
            LlmServiceFactory.ResolveEmbedModel().Should().Be("hf:custom/embed/model.gguf");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", original);
        }
    }

    [Fact]
    public void ResolveEmbedModel_ConfigOverridesEnvVar()
    {
        var original = Environment.GetEnvironmentVariable("QMD_EMBED_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", "hf:env/embed/model.gguf");
            LlmServiceFactory.ResolveEmbedModel("hf:config/embed/model.gguf")
                .Should().Be("hf:config/embed/model.gguf");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", original);
        }
    }

    [Fact]
    public void ResolveRerankModel_EnvVarOverridesDefault()
    {
        var original = Environment.GetEnvironmentVariable("QMD_RERANK_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("QMD_RERANK_MODEL", "hf:custom/rerank/model.gguf");
            LlmServiceFactory.ResolveRerankModel().Should().Be("hf:custom/rerank/model.gguf");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_RERANK_MODEL", original);
        }
    }

    [Fact]
    public void ResolveGenerateModel_EnvVarOverridesDefault()
    {
        var original = Environment.GetEnvironmentVariable("QMD_GENERATE_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("QMD_GENERATE_MODEL", "hf:custom/gen/model.gguf");
            LlmServiceFactory.ResolveGenerateModel().Should().Be("hf:custom/gen/model.gguf");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_GENERATE_MODEL", original);
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

            // Default model is now Qwen3 — should still use instruct format
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", null);
            var defaultFormatted = EmbeddingFormatter.FormatQueryForEmbedding("test query");
            defaultFormatted.Should().Contain("Instruct:");

            // Explicitly set to a non-Qwen3 model — should use task prefix format
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", "hf:org/embeddinggemma-300M-GGUF/model.gguf");
            var gemmaFormatted = EmbeddingFormatter.FormatQueryForEmbedding("test query");
            gemmaFormatted.Should().Contain("task: search result");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", original);
        }
    }
}
