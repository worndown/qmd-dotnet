using Qmd.Core.Models;

namespace Qmd.Core.Embedding;

internal interface IEmbeddingRepository
{
    List<PendingEmbeddingDoc> GetPendingEmbeddingDocs();
    List<EmbeddingDoc> GetEmbeddingDocsForBatch(List<PendingEmbeddingDoc> batch);
    void InsertEmbedding(string hash, int seq, int pos, float[] embedding, string model, string createdAt);
    void ClearAllEmbeddings();
    void ResetVecTableCache();
}
