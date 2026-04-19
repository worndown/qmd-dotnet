using LLama;
using LLama.Common;
using LLama.Sampling;
using Qmd.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace Qmd.Core.Llm;

/// <summary>
/// LLamaSharp implementation of ILlmService.
/// Manages model loading, embedding contexts, and inference.
/// </summary>
internal class LlamaSharpService : ILlmService
{
    private readonly string embedModelUri;
    private readonly string generateModelUri;
    private readonly string rerankModelUri;
    private readonly ModelResolver modelResolver;
    private readonly int expandContextSize;

    // Embedding state
    private LLamaWeights? embedWeights;
    private string? embedModelPath;
    private Task<LLamaWeights>? embedWeightsLoadTask;
    private List<LLamaEmbedder> embedContexts = [];
    private Task<List<LLamaEmbedder>>? embedContextsCreateTask;

    // Generation state
    private LLamaWeights? generateWeights;
    private string? generateModelPath;
    private Task<LLamaWeights>? generateWeightsLoadTask;

    // Reranking state
    private LLamaWeights? rerankWeights;
    private string? rerankModelPath;
    private Task<LLamaWeights>? rerankWeightsLoadTask;
    private List<LLamaReranker> rerankContexts = [];
    private Task<List<LLamaReranker>>? rerankContextsCreateTask;

    private bool disposed;

    private const int RerankTargetDocsPerContext = 10;

    public string EmbedModelName => this.embedModelUri;

    /// <summary>Resolved expand context size (config > env > default 2048).</summary>
    public int ExpandContextSize => this.expandContextSize;

    /// <summary>Create a new LlamaSharp LLM service.</summary>
    /// <param name="options">Model URIs, cache directory, and context size overrides.</param>
    public LlamaSharpService(LlamaSharpOptions? options = null)
    {
        options ??= new LlamaSharpOptions();
        this.embedModelUri = LlmServiceFactory.ResolveEmbedModel(options.EmbedModel);
        this.generateModelUri = LlmServiceFactory.ResolveGenerateModel(options.GenerateModel);
        this.rerankModelUri = LlmServiceFactory.ResolveRerankModel(options.RerankModel);
        this.modelResolver = new ModelResolver(cacheDir: options.ModelCacheDir);

        this.expandContextSize = ResolveExpandContextSize(options.ExpandContextSize);
    }

    private const int DefaultExpandContextSize = 2048;

    /// <summary>Resolve expand context size from config value, <c>QMD_EXPAND_CONTEXT_SIZE</c> env var, or default (2048).</summary>
    /// <param name="configValue">Explicit config override, or <c>null</c> to fall through to env/default.</param>
    internal static int ResolveExpandContextSize(int? configValue)
    {
        if (configValue.HasValue)
        {
            if (configValue.Value <= 0)
                throw new ArgumentException(
                    $"Invalid expandContextSize: {configValue.Value}. Must be a positive integer.");
            return configValue.Value;
        }

        var envValue = Environment.GetEnvironmentVariable("QMD_EXPAND_CONTEXT_SIZE")?.Trim();
        if (string.IsNullOrEmpty(envValue)) return DefaultExpandContextSize;

        if (int.TryParse(envValue, out var parsed) && parsed > 0)
            return parsed;

        // Invalid env var — silently use default
        return DefaultExpandContextSize;
    }

    #region Embedding

    /// <summary>Generate a vector embedding for a single text.</summary>
    /// <param name="text">Input text to embed.</param>
    /// <param name="options">Optional model override.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<EmbeddingResult?> EmbedAsync(string text, EmbedOptions? options = null, CancellationToken ct = default)
    {
        var embedder = await this.EnsureEmbedContextAsync(ct);
        text = this.TruncateToEmbedContextSize(text);
        var embeddings = await embedder.GetEmbeddings(text, ct);
        return new EmbeddingResult(embeddings[0], options?.Model ?? this.embedModelUri);
    }

    /// <summary>Generate vector embeddings for multiple texts, parallelized across available contexts.</summary>
    /// <param name="texts">Input texts to embed.</param>
    /// <param name="options">Optional model override.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<List<EmbeddingResult?>> EmbedBatchAsync(List<string> texts, EmbedOptions? options = null, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        // Truncate all texts to fit within embedding context size
        texts = texts.Select(this.TruncateToEmbedContextSize).ToList();

        var contexts = await this.EnsureEmbedContextsAsync(ct);
        var n = contexts.Count;

        if (n == 1)
        {
            var results = new List<EmbeddingResult?>();
            foreach (var text in texts)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var embeddings = await contexts[0].GetEmbeddings(text, ct);
                    results.Add(new EmbeddingResult(embeddings[0], options?.Model ?? this.embedModelUri));
                }
                catch
                {
                    // Per-text embedding failure — null signals failure for this item
                    results.Add(null);
                }
            }
            return results;
        }

        var chunkSize = (int)Math.Ceiling(texts.Count / (double)n);
        var tasks = Enumerable.Range(0, n).Select(async i =>
        {
            var chunk = texts.Skip(i * chunkSize).Take(chunkSize).ToList();
            var embedder = contexts[i];
            var results = new List<EmbeddingResult?>();
            foreach (var text in chunk)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var embeddings = await embedder.GetEmbeddings(text, ct);
                    results.Add(new EmbeddingResult(embeddings[0], options?.Model ?? this.embedModelUri));
                }
                catch
                {
                    // Per-text embedding failure — null signals failure for this item
                    results.Add(null);
                }
            }
            return results;
        }).ToArray();

        var allResults = await Task.WhenAll(tasks);
        return allResults.SelectMany(r => r).ToList();
    }

    /// <summary>Count tokens in a text string using the embedding model tokenizer.</summary>
    /// <param name="text">Text to tokenize.</param>
    /// <returns>Token count, or a character-based estimate if the model is not loaded.</returns>
    public int CountTokens(string text)
    {
        if (this.embedWeights == null)
            return (int)Math.Ceiling(text.Length / 4.0); // Fallback

        var tokens = this.embedWeights.NativeHandle.Tokenize(text, false, false, System.Text.Encoding.UTF8);
        return tokens.Length;
    }

    #endregion

    #region Generation

    /// <summary>Run generic text completion. Not currently used by any command.</summary>
    /// <param name="prompt">Prompt to complete.</param>
    /// <param name="options">Max tokens, temperature, and model overrides.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<GenerateResult?> GenerateAsync(string prompt, GenerateOptions? options = null, CancellationToken ct = default)
    {
        var weights = await this.EnsureGenerateWeightsAsync(ct);

        var maxTokens = options?.MaxTokens ?? 150;
        // Qwen3 recommended: temp=0.7, topP=0.8, topK=20 for non-thinking mode
        // DO NOT use greedy decoding (temp=0) - causes repetition loops
        var temperature = options?.Temperature ?? 0.7;

        var modelParams = new ModelParams(this.generateModelPath!)
        {
            ContextSize = (uint)this.expandContextSize,
        };

        var executor = new StatelessExecutor(weights, modelParams)
        {
            ApplyTemplate = true,
        };

        var inferenceParams = new InferenceParams
        {
            MaxTokens = maxTokens,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = (float)temperature,
                TopK = 20,
                TopP = 0.8f,
            },
        };

        var result = "";
        await foreach (var text in executor.InferAsync(prompt, inferenceParams, ct))
        {
            result += text;
        }

        return new GenerateResult(result, this.generateModelUri, null, true);
    }

    #endregion

    #region Query expansion

    /// <summary>Expand a search query into multiple typed search strategies (lex/vec/hyde) using GBNF-constrained generation.</summary>
    /// <param name="query">User search query to expand.</param>
    /// <param name="options">Context hint, lexical inclusion flag.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<List<QueryExpansion>> ExpandQueryAsync(string query, ExpandQueryOptions? options = null, CancellationToken ct = default)
    {
        try
        {
            var weights = await this.EnsureGenerateWeightsAsync(ct);
            var includeLexical = options?.IncludeLexical ?? true;

            // GBNF grammar constraining output to typed query lines
            var grammar = new Grammar(
                @"root ::= line+
line ::= type "": "" content ""\n""
type ::= ""lex"" | ""vec"" | ""hyde""
content ::= [^\n]+
", "root");

            var prompt = options?.Context != null
                ? $"/no_think Expand this search query: {query}\nQuery intent: {options.Context}"
                : $"/no_think Expand this search query: {query}";

            var modelParams = new ModelParams(this.generateModelPath!)
            {
                ContextSize = (uint)this.expandContextSize,
            };

            var executor = new StatelessExecutor(weights, modelParams)
            {
                ApplyTemplate = true,
            };

            var inferenceParams = new InferenceParams
            {
                MaxTokens = 600,
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = 0.0f,
                    PresencePenalty = 0.5f,
                    PenaltyCount = 64,
                    Grammar = grammar,
                },
            };

            var result = "";
            await foreach (var text in executor.InferAsync(prompt, inferenceParams, ct))
            {
                result += text;
            }

            var lines = result.Trim().Split('\n');
            var queryLower = query.ToLowerInvariant();
            var queryTerms = Regex.Replace(queryLower, @"[^a-z0-9\s]", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            bool HasQueryTerm(string text)
            {
                var lower = text.ToLowerInvariant();
                if (queryTerms.Length == 0) return true;
                return queryTerms.Any(term => lower.Contains(term));
            }

            var queryables = new List<QueryExpansion>();
            foreach (var line in lines)
            {
                var colonIdx = line.IndexOf(':');
                if (colonIdx == -1) continue;
                var type = line[..colonIdx].Trim();
                if (type is not ("lex" or "vec" or "hyde")) continue;
                var text = line[(colonIdx + 1)..].Trim();
                if (!HasQueryTerm(text)) continue;

                var queryType = type switch
                {
                    "lex" => QueryType.Lex,
                    "vec" => QueryType.Vec,
                    "hyde" => QueryType.Hyde,
                    _ => QueryType.Lex
                };
                queryables.Add(new QueryExpansion(queryType, text));
            }

            var filtered = includeLexical
                ? queryables
                : queryables.Where(q => q.Type != QueryType.Lex).ToList();

            if (filtered.Count > 0) return filtered;

            // Fallback if LLM expansion produced nothing usable
            var fallback = new List<QueryExpansion>
            {
                new(QueryType.Hyde, $"Information about {query}"),
                new(QueryType.Lex, query),
                new(QueryType.Vec, query),
            };
            return includeLexical ? fallback : fallback.Where(q => q.Type != QueryType.Lex).ToList();
        }
        catch
        {
            // Query expansion failed — fall back to direct query (degraded but functional)
            var fallback = new List<QueryExpansion> { new(QueryType.Vec, query) };
            if (options?.IncludeLexical ?? true) fallback.Insert(0, new QueryExpansion(QueryType.Lex, query));
            return fallback;
        }
    }

    #endregion

    #region Reranking

    /// <summary>Score and reorder documents by relevance to a query using a cross-encoder reranker.</summary>
    /// <param name="query">Search query to rank against.</param>
    /// <param name="documents">Documents to rerank.</param>
    /// <param name="options">Optional reranking parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<RerankResult> RerankAsync(string query, List<RerankDocument> documents, RerankOptions? options = null, CancellationToken ct = default)
    {
        if (documents.Count == 0) return new RerankResult([], this.rerankModelUri);

        var rerankers = await this.EnsureRerankContextsAsync(ct);
        var model = await this.EnsureRerankWeightsAsync(ct);

        // Truncate documents that would exceed rerank context size
        // Budget = contextSize - template overhead - query tokens
        var queryTokens = model.Tokenize(query, false, false, System.Text.Encoding.UTF8).Length;
        var maxDocTokens = LlmConstants.RerankContextSize - LlmConstants.RerankTemplateOverhead - queryTokens;

        var truncatedDocs = documents.Select(doc =>
        {
            var tokens = model.Tokenize(doc.Text, false, false, System.Text.Encoding.UTF8);
            if (tokens.Length <= maxDocTokens) return doc;
            var truncatedTokens = tokens[..maxDocTokens];
            var sb = new StringBuilder();
            foreach (var token in truncatedTokens)
                sb.Append(model.Vocab.LLamaTokenToString(token, false));
            return doc with { Text = sb.ToString() };
        }).ToList();

        // Deduplicate identical texts before scoring
        var textToDocs = new Dictionary<string, List<(string File, int Index)>>();
        for (int i = 0; i < truncatedDocs.Count; i++)
        {
            var text = truncatedDocs[i].Text;
            if (!textToDocs.TryGetValue(text, out var list))
            {
                list = [];
                textToDocs[text] = list;
            }
            list.Add((truncatedDocs[i].File, i));
        }

        var uniqueTexts = textToDocs.Keys.ToList();

        // Split across contexts for parallel evaluation
        var activeContextCount = Math.Max(1, Math.Min(rerankers.Count,
            (int)Math.Ceiling(uniqueTexts.Count / (double)RerankTargetDocsPerContext)));
        var activeContexts = rerankers.Take(activeContextCount).ToList();
        var chunkSize = (int)Math.Ceiling(uniqueTexts.Count / (double)activeContexts.Count);

        var chunks = Enumerable.Range(0, activeContexts.Count)
            .Select(i => uniqueTexts.Skip(i * chunkSize).Take(chunkSize).ToList())
            .Where(chunk => chunk.Count > 0)
            .ToList();

        var scoreTasks = chunks.Select(async (chunk, i) =>
            await activeContexts[i].GetRelevanceScores(query, chunk, true, ct)
        ).ToArray();

        var allScores = await Task.WhenAll(scoreTasks);
        var flatScores = allScores.SelectMany(s => s).ToArray();

        // Reassemble scores and sort descending
        var ranked = uniqueTexts
            .Select((text, i) => (Text: text, Score: flatScores[i]))
            .OrderByDescending(x => x.Score)
            .ToList();

        // Map back to original documents
        var results = new List<RerankDocumentResult>();
        foreach (var item in ranked)
        {
            if (!textToDocs.TryGetValue(item.Text, out var docInfos)) continue;
            foreach (var (file, index) in docInfos)
            {
                results.Add(new RerankDocumentResult(file, item.Score, index));
            }
        }

        return new RerankResult(results, this.rerankModelUri);
    }

    #endregion

    #region Model loading — Embedding

    /// <summary>Return cached embedding weights, or load them on first call. Coalesces concurrent callers.</summary>
    private async Task<LLamaWeights> EnsureEmbedWeightsAsync(CancellationToken ct = default)
    {
        if (this.embedWeights != null) return this.embedWeights;
        if (this.embedWeightsLoadTask != null) return await this.embedWeightsLoadTask;

        this.embedWeightsLoadTask = this.LoadEmbedWeightsAsync(ct);
        try { return await this.embedWeightsLoadTask; }
        finally {
            this.embedWeightsLoadTask = null; }
    }

    /// <summary>Resolve the model URI and load embedding weights from disk.</summary>
    private async Task<LLamaWeights> LoadEmbedWeightsAsync(CancellationToken ct)
    {
        this.embedModelPath = await this.modelResolver.ResolveModelFileAsync(this.embedModelUri, ct: ct);
        var modelParams = new ModelParams(this.embedModelPath)
        {
            Embeddings = true,
            ContextSize = (uint)LlmConstants.EmbedContextSize,
        };
        this.embedWeights = await LLamaWeights.LoadFromFileAsync(modelParams, ct, new Progress<float>(_ => { }));
        return this.embedWeights;
    }

    /// <summary>Return the first available embedding context (convenience for single-text operations).</summary>
    private async Task<LLamaEmbedder> EnsureEmbedContextAsync(CancellationToken ct = default)
    {
        var contexts = await this.EnsureEmbedContextsAsync(ct);
        return contexts[0];
    }

    /// <summary>Return cached embedding contexts, or create the pool on first call. Coalesces concurrent callers.</summary>
    private async Task<List<LLamaEmbedder>> EnsureEmbedContextsAsync(CancellationToken ct = default)
    {
        if (this.embedContexts.Count > 0) return this.embedContexts;
        if (this.embedContextsCreateTask != null) return await this.embedContextsCreateTask;

        this.embedContextsCreateTask = this.CreateEmbedContextsAsync(ct);
        try { return await this.embedContextsCreateTask; }
        finally {
            this.embedContextsCreateTask = null; }
    }

    /// <summary>Create a pool of embedding contexts sized by CPU parallelism. Stops gracefully if a context fails to allocate.</summary>
    private async Task<List<LLamaEmbedder>> CreateEmbedContextsAsync(CancellationToken ct)
    {
        var weights = await this.EnsureEmbedWeightsAsync(ct);
        var parallelism = ComputeParallelism();

        for (int i = 0; i < parallelism; i++)
        {
            try
            {
                var embedder = new LLamaEmbedder(weights, new ModelParams(this.embedModelPath!)
                {
                    Embeddings = true,
                    ContextSize = (uint)LlmConstants.EmbedContextSize,
                });
                this.embedContexts.Add(embedder);
            }
            catch
            {
                if (this.embedContexts.Count == 0)
                    throw new InvalidOperationException("Failed to create any embedding context");
                break;
            }
        }

        return this.embedContexts;
    }

    #endregion

    #region Model loading — Generation

    /// <summary>Return cached generation weights, or load them on first call. Coalesces concurrent callers.</summary>
    private async Task<LLamaWeights> EnsureGenerateWeightsAsync(CancellationToken ct = default)
    {
        if (this.generateWeights != null) return this.generateWeights;
        if (this.generateWeightsLoadTask != null) return await this.generateWeightsLoadTask;

        this.generateWeightsLoadTask = this.LoadGenerateWeightsAsync(ct);
        try { return await this.generateWeightsLoadTask; }
        finally {
            this.generateWeightsLoadTask = null; }
    }

    /// <summary>Resolve the model URI and load generation weights from disk.</summary>
    private async Task<LLamaWeights> LoadGenerateWeightsAsync(CancellationToken ct)
    {
        this.generateModelPath = await this.modelResolver.ResolveModelFileAsync(this.generateModelUri, ct: ct);
        var modelParams = new ModelParams(this.generateModelPath)
        {
            ContextSize = (uint)this.expandContextSize,
        };
        this.generateWeights = await LLamaWeights.LoadFromFileAsync(modelParams, ct, new Progress<float>(_ => { }));
        return this.generateWeights;
    }

    #endregion

    #region Model loading — Reranking

    /// <summary>Return cached rerank weights, or load them on first call. Coalesces concurrent callers.</summary>
    private async Task<LLamaWeights> EnsureRerankWeightsAsync(CancellationToken ct = default)
    {
        if (this.rerankWeights != null) return this.rerankWeights;
        if (this.rerankWeightsLoadTask != null) return await this.rerankWeightsLoadTask;

        this.rerankWeightsLoadTask = this.LoadRerankWeightsAsync(ct);
        try { return await this.rerankWeightsLoadTask; }
        finally {
            this.rerankWeightsLoadTask = null; }
    }

    /// <summary>Resolve the model URI and load rerank weights from disk.</summary>
    private async Task<LLamaWeights> LoadRerankWeightsAsync(CancellationToken ct)
    {
        this.rerankModelPath = await this.modelResolver.ResolveModelFileAsync(this.rerankModelUri, ct: ct);
        var modelParams = new ModelParams(this.rerankModelPath)
        {
            ContextSize = (uint)LlmConstants.RerankContextSize,
        };
        this.rerankWeights = await LLamaWeights.LoadFromFileAsync(modelParams, ct, new Progress<float>(_ => { }));
        return this.rerankWeights;
    }

    /// <summary>Return cached reranker contexts, or create the pool on first call. Coalesces concurrent callers.</summary>
    private async Task<List<LLamaReranker>> EnsureRerankContextsAsync(CancellationToken ct = default)
    {
        if (this.rerankContexts.Count > 0) return this.rerankContexts;
        if (this.rerankContextsCreateTask != null) return await this.rerankContextsCreateTask;

        this.rerankContextsCreateTask = this.CreateRerankContextsAsync(ct);
        try { return await this.rerankContextsCreateTask; }
        finally {
            this.rerankContextsCreateTask = null; }
    }

    /// <summary>Create a pool of reranker contexts (up to 4). Falls back to non-FlashAttention if first context fails.</summary>
    private async Task<List<LLamaReranker>> CreateRerankContextsAsync(CancellationToken ct)
    {
        var weights = await this.EnsureRerankWeightsAsync(ct);
        var n = Math.Min(ComputeParallelism(), 4);

        for (int i = 0; i < n; i++)
        {
            try
            {
                var contextParams = new ModelParams(this.rerankModelPath!)
                {
                    ContextSize = (uint)LlmConstants.RerankContextSize,
                    BatchSize = (uint)LlmConstants.RerankContextSize,
                    UBatchSize = (uint)LlmConstants.RerankContextSize,
                    PoolingType = LLama.Native.LLamaPoolingType.Rank,
                    FlashAttention = true,
                };
                var reranker = new LLamaReranker(weights, contextParams);
                this.rerankContexts.Add(reranker);
            }
            catch
            {
                if (this.rerankContexts.Count == 0)
                {
                    // FlashAttention not supported — retry without it
                    try
                    {
                        var fallbackParams = new ModelParams(this.rerankModelPath!)
                        {
                            ContextSize = (uint)LlmConstants.RerankContextSize,
                            BatchSize = (uint)LlmConstants.RerankContextSize,
                            UBatchSize = (uint)LlmConstants.RerankContextSize,
                            PoolingType = LLama.Native.LLamaPoolingType.Rank,
                        };
                        this.rerankContexts.Add(new LLamaReranker(weights, fallbackParams));
                    }
                    catch (Exception ex2)
                    {
                        throw new InvalidOperationException("Failed to create any rerank context", ex2);
                    }
                }
                break;
            }
        }

        return this.rerankContexts;
    }

    #endregion

    #region Helpers

    /// <summary>Compute context pool size based on CPU core count (1–4, CPU-only heuristic).</summary>
    private static int ComputeParallelism()
    {
        var cores = Environment.ProcessorCount;
        return Math.Max(1, Math.Min(4, cores / 4));
    }

    /// <summary>
    /// Truncate text to fit within embedding context size, with 4-token safety margin.
    /// Uses the model's tokenizer when available; falls back to character estimate.
    /// </summary>
    private string TruncateToEmbedContextSize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var maxTokens = LlmConstants.EmbedContextSize - 4; // safety margin

        if (this.embedWeights != null)
        {
            var tokens = this.embedWeights.NativeHandle.Tokenize(text, false, false, Encoding.UTF8);
            if (tokens.Length <= maxTokens) return text;

            // Binary search for the right character cutoff
            var ratio = (double)text.Length / tokens.Length;
            var estimatedChars = (int)(maxTokens * ratio);
            estimatedChars = Math.Min(estimatedChars, text.Length);

            // Verify and shrink if needed
            while (estimatedChars > 0)
            {
                var candidate = text[..estimatedChars];
                var count = this.embedWeights.NativeHandle.Tokenize(candidate, false, false, Encoding.UTF8).Length;
                if (count <= maxTokens) return candidate;
                estimatedChars = (int)(estimatedChars * 0.9);
            }
            return text[..Math.Min(text.Length, maxTokens * 3)]; // extreme fallback
        }

        // Character-based fallback (4 chars/token estimate)
        var maxChars = maxTokens * 4;
        return text.Length <= maxChars ? text : text[..maxChars];
    }

    #endregion

    #region Lifecycle

    /// <summary>Dispose all loaded models, embedders, and reranker contexts.</summary>
    public async ValueTask DisposeAsync()
    {
        if (this.disposed) return;
        this.disposed = true;

        foreach (var ctx in this.embedContexts)
            ctx.Dispose();
        this.embedContexts.Clear();

        foreach (var ctx in this.rerankContexts)
            ctx.Dispose();
        this.rerankContexts.Clear();

        this.embedWeights?.Dispose();
        this.embedWeights = null;

        this.generateWeights?.Dispose();
        this.generateWeights = null;

        this.rerankWeights?.Dispose();
        this.rerankWeights = null;

        GC.SuppressFinalize(this);
    }

    #endregion
}

/// <summary>Configuration options for <see cref="LlamaSharpService"/>.</summary>
public class LlamaSharpOptions
{
    /// <summary>HuggingFace URI or local path for the embedding model. Falls back to <c>QMD_EMBED_MODEL</c> env var.</summary>
    public string? EmbedModel { get; init; }

    /// <summary>HuggingFace URI or local path for the generation model. Falls back to <c>QMD_GENERATE_MODEL</c> env var.</summary>
    public string? GenerateModel { get; init; }

    /// <summary>HuggingFace URI or local path for the reranking model. Falls back to <c>QMD_RERANK_MODEL</c> env var.</summary>
    public string? RerankModel { get; init; }

    /// <summary>Directory to cache downloaded model files.</summary>
    public string? ModelCacheDir { get; init; }

    /// <summary>
    /// Context size for query expansion model. Overrides QMD_EXPAND_CONTEXT_SIZE env var.
    /// Default: 2048. Must be a positive integer.
    /// </summary>
    public int? ExpandContextSize { get; init; }
}
