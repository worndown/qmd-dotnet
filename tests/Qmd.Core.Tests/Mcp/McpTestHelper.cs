using Qmd.Core.Configuration;
using Qmd.Core.Database;
using Qmd.Core.Llm;
using Qmd.Core.Models;
using Qmd.Core.Store;

namespace Qmd.Core.Tests.Mcp;

/// <summary>
/// Creates in-memory stores seeded with test documents for MCP tool testing.
/// </summary>
internal static class McpTestHelper
{
    public static IQmdStore CreateSeededStore()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                ["docs"] = new Collection
                {
                    Path = "/test/docs",
                    Pattern = "**/*.md",
                    Context = new Dictionary<string, string>
                    {
                        ["/meetings"] = "Meeting notes and transcripts"
                    }
                },
                ["code"] = new Collection { Path = "/test/code", Pattern = "**/*.ts" },
            }
        };

        var db = new SqliteDatabase(":memory:");
        var configManager = new ConfigManager(new InlineConfigSource(config));
        var store = new QmdStore(db, configManager, new MockLlmService());

        var now = DateTime.UtcNow.ToString("o");

        Seed(store, "docs", "readme.md",
            "# Project README\n\nThis is the main readme file.\nIt contains project documentation.\nLine 4\nLine 5\nLine 6\nLine 7\nLine 8\nLine 9\nLine 10",
            now);

        Seed(store, "docs", "api/guide.md",
            "# API Guide\n\nHow to use the API.\nEndpoint: GET /health\nEndpoint: POST /query",
            now);

        Seed(store, "code", "index.ts",
            "import { serve } from 'bun';\n\nconst server = serve({ port: 3000 });\nconsole.log('listening');",
            now);

        Seed(store, "docs", "meetings/meeting-2024-01.md",
            "# January Meeting Notes\n\nDiscussed Q1 goals and roadmap.\n\n## Action Items\n\n- Review budget\n- Hire new team members",
            now);

        Seed(store, "docs", "meetings/meeting-2024-02.md",
            "# February Meeting Notes\n\nFollowed up on Q1 progress.\n\n## Updates\n\n- Budget approved\n- Two candidates interviewed",
            now);

        // Large document (~15KB) for size-filtering tests
        var largeContent = string.Join("\n", Enumerable.Range(1, 500).Select(i => $"Line {i}: Lorem ipsum dolor sit amet"));
        Seed(store, "docs", "large-file.md", largeContent, now);

        return store;
    }

    public static IQmdStore CreateEmptyStore()
    {
        var db = new SqliteDatabase(":memory:");
        var configManager = new ConfigManager(new InlineConfigSource(new CollectionConfig()));
        return new QmdStore(db, configManager, new MockLlmService());
    }

    /// <summary>
    /// Minimal mock LLM service for testing. Returns passthrough for expansion, empty for reranking.
    /// </summary>
    private class MockLlmService : ILlmService
    {
        public string EmbedModelName => "mock";

        public Task<EmbeddingResult?> EmbedAsync(string text, EmbedOptions? options = null, CancellationToken ct = default)
            => Task.FromResult<EmbeddingResult?>(null);

        public Task<List<EmbeddingResult?>> EmbedBatchAsync(List<string> texts, EmbedOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(texts.Select(_ => (EmbeddingResult?)null).ToList());

        public int CountTokens(string text) => (int)Math.Ceiling(text.Length / 4.0);

        public Task<GenerateResult?> GenerateAsync(string prompt, GenerateOptions? options = null, CancellationToken ct = default)
            => Task.FromResult<GenerateResult?>(null);

        public Task<RerankResult> RerankAsync(string query, List<RerankDocument> documents, RerankOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new RerankResult([], "mock"));

        public Task<List<QueryExpansion>> ExpandQueryAsync(string query, ExpandQueryOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new List<QueryExpansion> { new(QueryType.Lex, query) });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static void Seed(QmdStore store, string collection, string path, string content, string timestamp)
    {
        var hash = store.HashContent(content);
        store.InsertContent(hash, content, timestamp);
        var title = store.ExtractTitle(content, path);
        store.InsertDocument(collection, path, title, hash, timestamp, timestamp);
    }
}
