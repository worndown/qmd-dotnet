using System.Diagnostics;
using Qmd.Core.Chunking;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Llm;
using Qmd.Core.Models;

namespace Qmd.Core.Embedding;

/// <summary>
/// Generates vector embeddings for documents that need them.
/// </summary>
public static class EmbeddingPipeline
{
    public static async Task<EmbedResult> GenerateEmbeddingsAsync(
        IQmdDatabase db,
        ILlmService llmService,
        EmbedPipelineOptions? options = null,
        Action<int>? ensureVecTable = null)
    {
        options ??= new EmbedPipelineOptions();

        if (options.MaxDocsPerBatch <= 0)
            throw new ArgumentException("maxDocsPerBatch must be positive", nameof(options));
        if (options.MaxBatchBytes <= 0)
            throw new ArgumentException("maxBatchBytes must be positive", nameof(options));

        var model = options.Model ?? LlmConstants.DefaultEmbedModel;
        var sw = Stopwatch.StartNew();

        if (options.Force)
            EmbeddingOperations.ClearAllEmbeddings(db);

        var pending = EmbeddingOperations.GetPendingEmbeddingDocs(db);
        if (pending.Count == 0)
            return new EmbedResult(0, 0, 0, sw.ElapsedMilliseconds);

        var batches = BatchAssembler.BuildBatches(pending, options.MaxDocsPerBatch, options.MaxBatchBytes);

        int totalChunksEmbedded = 0;
        int totalChunksDiscovered = 0;
        int totalDocsProcessed = 0;
        int totalErrors = 0;
        long totalBytesProcessed = 0;
        long totalBytes = pending.Sum(d => d.Bytes);

        var tokenizer = new LlmServiceTokenizer(llmService);

        foreach (var batch in batches)
        {
            options.CancellationToken.ThrowIfCancellationRequested();

            var docs = EmbeddingOperations.GetEmbeddingDocsForBatch(db, batch);

            foreach (var doc in docs)
            {
                options.CancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var title = TitleExtractor.ExtractTitle(doc.Body, doc.Path);
                    var chunks = DocumentChunker.ChunkDocumentByTokens(
                        tokenizer, doc.Body,
                        filepath: doc.Path,
                        chunkStrategy: options.ChunkStrategy,
                        cancellationToken: options.CancellationToken);
                    totalChunksDiscovered += chunks.Count;

                    var formattedTexts = chunks.Select(c =>
                        EmbeddingFormatter.FormatDocForEmbedding(c.Text, title, model)
                    ).ToList();

                    List<EmbeddingResult?> embeddings;
                    try
                    {
                        embeddings = await llmService.EmbedBatchAsync(formattedTexts,
                            new EmbedOptions { Model = model }, options.CancellationToken);
                    }
                    catch (Exception batchEx)
                    {
                        // Batch failed — fallback to per-chunk embedding (matches TS behavior)
                        Console.Error.WriteLine($"Batch embed failed for {doc.Path}, retrying per-chunk: {batchEx.Message}");
                        embeddings = new List<EmbeddingResult?>(new EmbeddingResult?[formattedTexts.Count]);
                        for (int j = 0; j < formattedTexts.Count; j++)
                        {
                            try
                            {
                                embeddings[j] = await llmService.EmbedAsync(formattedTexts[j],
                                    new EmbedOptions { Model = model }, options.CancellationToken);
                            }
                            catch { embeddings[j] = null; }
                        }
                    }

                    for (int i = 0; i < chunks.Count; i++)
                    {
                        if (embeddings[i] != null)
                        {
                            var emb = embeddings[i]!;

                            // Ensure vec table exists with correct dimensions
                            ensureVecTable?.Invoke(emb.Embedding.Length);
                            EmbeddingOperations.ResetVecTableCache();

                            EmbeddingOperations.InsertEmbedding(db,
                                doc.Hash, i, chunks[i].Pos,
                                emb.Embedding, model,
                                DateTime.UtcNow.ToString("o"));
                            totalChunksEmbedded++;
                        }
                        else
                        {
                            totalErrors++;
                        }
                    }

                    totalDocsProcessed++;
                    totalBytesProcessed += doc.Bytes;

                    options.OnProgress?.Invoke(new EmbedProgress(
                        totalChunksEmbedded,
                        totalChunksDiscovered,
                        totalBytesProcessed,
                        totalBytes,
                        totalErrors));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Embedding error for {doc.Path}: {ex.Message}");
                    totalErrors++;
                }

                // Error rate throttling on chunks (matches TS BATCH_SIZE = 32)
                var totalProcessed = totalChunksEmbedded + totalErrors;
                if (totalProcessed >= 32 && totalErrors > totalProcessed * 0.8)
                {
                    Console.Error.WriteLine($"Aborting: error rate too high ({totalErrors}/{totalProcessed})");
                    break;
                }
            }
        }

        return new EmbedResult(totalDocsProcessed, totalChunksEmbedded, totalErrors, sw.ElapsedMilliseconds);
    }
}
