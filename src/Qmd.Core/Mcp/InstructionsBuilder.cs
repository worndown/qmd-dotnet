using System.Text;
using Qmd.Core.Models;

namespace Qmd.Core.Mcp;

/// <summary>
/// Builds dynamic LLM instructions from index state.
/// Injected into MCP server prompts at initialization.
/// </summary>
internal static class InstructionsBuilder
{
    public static async Task<string> BuildAsync(IQmdStore store)
    {
        var status = await store.GetStatusAsync();
        var sb = new StringBuilder();

        sb.AppendLine($"QMD knowledge base with {status.TotalDocuments} indexed documents.");
        sb.AppendLine();

        if (status.Collections.Count > 0)
        {
            sb.AppendLine("Collections:");
            var contexts = await store.ListContextsAsync();
            var contextsByCollection = contexts
                .GroupBy(c => c.Collection)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var c in status.Collections)
            {
                sb.AppendLine($"  - {c.Name} ({c.Documents} docs)");
                if (contextsByCollection.TryGetValue(c.Name, out var collContexts))
                {
                    foreach (var (_, path, ctx) in collContexts)
                    {
                        var prefix = path == "/" ? "" : $" [{path}]";
                        sb.AppendLine($"    {prefix}{ctx}");
                    }
                }
            }
            sb.AppendLine();
        }

        // Global context
        var globalCtx = await store.GetGlobalContextAsync();
        if (globalCtx != null)
        {
            sb.AppendLine($"Context: {globalCtx}");
            sb.AppendLine();
        }

        if (!status.HasVectorIndex)
        {
            sb.AppendLine("Note: Vector index not available. Use 'lex' queries (keyword search) only.");
            sb.AppendLine("Run 'qmd embed' to generate embeddings for semantic search.");
        }
        else if (status.NeedsEmbedding > 0)
        {
            sb.AppendLine($"Note: {status.NeedsEmbedding} documents need embedding. Run 'qmd embed' to update.");
        }

        sb.AppendLine();
        sb.AppendLine("Search: Use `query` with sub-queries (lex/vec/hyde):");
        sb.AppendLine("  - type:'lex' — BM25 keyword search (exact terms, fast)");
        sb.AppendLine("  - type:'vec' — semantic vector search (meaning-based)");
        sb.AppendLine("  - type:'hyde' — hypothetical document (write what the answer looks like)");
        sb.AppendLine();
        sb.AppendLine("  Always provide `intent` on every search call to disambiguate and improve snippets.");
        sb.AppendLine();
        sb.AppendLine("Examples:");
        sb.AppendLine("  Quick keyword lookup: [{type:'lex', query:'error handling'}]");
        sb.AppendLine("  Semantic search: [{type:'vec', query:'how to handle errors gracefully'}]");
        sb.AppendLine("  Best results: [{type:'lex', query:'error'}, {type:'vec', query:'error handling best practices'}]");
        sb.AppendLine("  With intent: searches=[{type:'lex', query:'performance'}], intent='web page load times'");
        sb.AppendLine();
        sb.AppendLine("Retrieval:");
        sb.AppendLine("  - `get` — single document by path or docid (#abc123). Supports line offset (`file.md:100`).");
        sb.AppendLine("  - `multi_get` — batch retrieve by glob (`journals/2025-05*.md`) or comma-separated list.");
        sb.AppendLine();
        sb.AppendLine("Tips:");
        sb.AppendLine("  - File paths in results are relative to their collection.");
        sb.AppendLine("  - Use `minScore: 0.5` to filter low-confidence results.");
        sb.AppendLine("  - Results include a `context` field describing the content type.");

        return sb.ToString();
    }
}
