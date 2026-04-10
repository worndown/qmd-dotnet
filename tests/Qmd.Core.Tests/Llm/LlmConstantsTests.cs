using FluentAssertions;
using Qmd.Core.Llm;
using Qmd.Llm;

namespace Qmd.Core.Tests.Llm;

/// <summary>
/// Port of test/llm.test.ts "LlamaCpp model resolution" and "expand context size config" tests.
/// Verifies that LlmConstants defaults match the TypeScript hardcoded values.
/// </summary>
public class LlmConstantsTests
{
    // =========================================================================
    // "uses hardcoded default when no config or env is set" — verify exact model URIs
    // =========================================================================

    [Fact]
    public void DefaultEmbedModel_MatchesTypeScriptHardcoded()
    {
        // TS: HARDCODED_EMBED = "hf:ggml-org/embeddinggemma-300M-GGUF/embeddinggemma-300M-Q8_0.gguf"
        LlmConstants.DefaultEmbedModel.Should().Be(
            "hf:ggml-org/embeddinggemma-300M-GGUF/embeddinggemma-300M-Q8_0.gguf");
    }

    [Fact]
    public void DefaultRerankModel_MatchesTypeScriptHardcoded()
    {
        // TS: HARDCODED_RERANK = "hf:ggml-org/Qwen3-Reranker-0.6B-Q8_0-GGUF/qwen3-reranker-0.6b-q8_0.gguf"
        LlmConstants.DefaultRerankModel.Should().Be(
            "hf:ggml-org/Qwen3-Reranker-0.6B-Q8_0-GGUF/qwen3-reranker-0.6b-q8_0.gguf");
    }

    [Fact]
    public void DefaultGenerateModel_MatchesTypeScriptHardcoded()
    {
        // TS: HARDCODED_GENERATE = "hf:tobil/qmd-query-expansion-1.7B-gguf/qmd-query-expansion-1.7B-q4_k_m.gguf"
        LlmConstants.DefaultGenerateModel.Should().Be(
            "hf:tobil/qmd-query-expansion-1.7B-gguf/qmd-query-expansion-1.7B-q4_k_m.gguf");
    }

    // =========================================================================
    // "context size config: uses default when no env" — verify default embed context size
    // =========================================================================

    [Fact]
    public void EmbedContextSize_DefaultIs2048()
    {
        // TS: "uses default expand context size when no config or env is set" — default is 2048
        LlmConstants.EmbedContextSize.Should().Be(2048);
    }

    // =========================================================================
    // Verify RerankContextSize matches TS reranker contextSize=4096
    // =========================================================================

    [Fact]
    public void RerankContextSize_DefaultIs4096()
    {
        // TS reranker creates contexts with contextSize=4096 (from PR #150 and store.ts)
        LlmConstants.RerankContextSize.Should().Be(4096);
    }

    // =========================================================================
    // Model resolution: env var overrides default (TS llm.test.ts)
    // =========================================================================

    [Fact]
    public void ModelResolution_EnvVarOverridesDefault()
    {
        // Ports: "env var overrides hardcoded default"
        var original = Environment.GetEnvironmentVariable("QMD_EMBED_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", "hf:custom/model/custom.gguf");
            var service = new Qmd.Llm.LlamaSharpService(new Qmd.Llm.LlamaSharpOptions());
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
        // Ports: "config overrides env var"
        var original = Environment.GetEnvironmentVariable("QMD_EMBED_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", "hf:env/model/env.gguf");
            var service = new Qmd.Llm.LlamaSharpService(new Qmd.Llm.LlamaSharpOptions
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
        // Ports: "uses hardcoded default when no config or env is set"
        var original = Environment.GetEnvironmentVariable("QMD_EMBED_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", null);
            var service = new Qmd.Llm.LlamaSharpService(new Qmd.Llm.LlamaSharpOptions());
            service.EmbedModelName.Should().Be(LlmConstants.DefaultEmbedModel);
        }
        finally
        {
            Environment.SetEnvironmentVariable("QMD_EMBED_MODEL", original);
        }
    }

    // =========================================================================
    // EmbeddingFormatter model resolution with env vars (TS llm.test.ts)
    // =========================================================================

    // =========================================================================
    // Expand context size resolution (TS llm.test.ts "expand context size config")
    // =========================================================================

    [Fact]
    public void ExpandContextSize_UsesDefault_WhenNoEnvOrConfig()
    {
        // Ports: "uses default expand context size"
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
        // Ports: "uses QMD_EXPAND_CONTEXT_SIZE when set"
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
        // Ports: "config value overrides env var"
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
        // Ports: "falls back to default and warns on invalid env var"
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
        // Ports: "throws when config expandContextSize is invalid"
        var act = () => LlamaSharpService.ResolveExpandContextSize(0);
        act.Should().Throw<ArgumentException>().WithMessage("*Invalid expandContextSize*");
    }

    [Fact]
    public void EmbeddingFormatter_EnvVarAffectsQueryFormatting()
    {
        // Ports: "QMD_EMBED_MODEL env var affects formatting"
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
