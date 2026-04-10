using Qmd.Core.Models;

namespace Qmd.Core.Embedding;

/// <summary>
/// Builds size-bounded batches of documents for embedding.
/// </summary>
public static class BatchAssembler
{
    public static List<List<PendingEmbeddingDoc>> BuildBatches(
        List<PendingEmbeddingDoc> docs,
        int maxDocsPerBatch = 64,
        int maxBatchBytes = 64 * 1024 * 1024)
    {
        var batches = new List<List<PendingEmbeddingDoc>>();
        var currentBatch = new List<PendingEmbeddingDoc>();
        long currentBytes = 0;

        foreach (var doc in docs)
        {
            var docBytes = Math.Max(0, doc.Bytes);
            var wouldExceedDocs = currentBatch.Count >= maxDocsPerBatch;
            var wouldExceedBytes = currentBatch.Count > 0 && (currentBytes + docBytes) > maxBatchBytes;

            if (wouldExceedDocs || wouldExceedBytes)
            {
                batches.Add(currentBatch);
                currentBatch = [];
                currentBytes = 0;
            }

            currentBatch.Add(doc);
            currentBytes += docBytes;
        }

        if (currentBatch.Count > 0)
            batches.Add(currentBatch);

        return batches;
    }
}
