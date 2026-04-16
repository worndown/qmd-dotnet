# How QMD Search Works

QMD provides three search commands, each trading speed for depth. This guide explains how they work, why certain defaults were chosen, and how to calibrate thresholds for your own corpus.

## Search Modes at a Glance

| Command | Method | Query expansion | Reranking | Speed | Best for |
|---------|--------|-----------------|-----------|-------|----------|
| `search` | BM25 keyword matching | No | No | Fast | Exact terms, known phrases |
| `vsearch` | Vector cosine similarity | Yes | No | Medium | Conceptual queries, synonyms |
| `query` | BM25 + vector + RRF fusion | Yes | Yes (LLM) | Slowest | Best overall accuracy |

---

## The Noise Floor Problem

Embedding models convert text into high-dimensional vectors. Ideally, unrelated documents would have cosine similarity near zero. In practice they don't — a phenomenon called **anisotropy** (also known as the representation degeneration problem). Embedding vectors for same-language text tend to cluster in narrow cones in the vector space, so even completely unrelated documents share a baseline similarity well above zero.

**The noise floor varies significantly by model:**

| Model | Unrelated-pair similarity | Source |
|-------|---------------------------|--------|
| all-MiniLM-L6-v2 (384d) | ~0.05-0.28 | [sbert.net docs](https://sbert.net/docs/sentence_transformer/usage/semantic_textual_similarity.html) |
| Raw BERT-base-uncased | ~0.44 | [Su et al. 2021](https://ar5iv.labs.arxiv.org/html/2104.05274) |
| BGE models | ~0.60+ | [BAAI model card](https://huggingface.co/BAAI/bge-reranker-base) |
| OpenAI text-embedding-ada-002 | 0.68-0.81 | [Blue Yonder](https://tech.blueyonder.com/text-embedding-and-cosine-similarity/) |
| embeddinggemma (300M) | ~0.45 (observed) | Empirical |
| Qwen3-Embedding-0.6B (QMD default) | ~0.32 (observed) | Empirical — use `profile-embeddings` to measure on your corpus |

This means a vector search for something completely absent from your documents will still return results with similarity scores around 0.45 — not because they're relevant, but because same-language text shares structural patterns in the embedding space.

Modern sentence transformers reduce anisotropy through contrastive learning, but do not eliminate it. The key takeaway: **a threshold that works for one model doesn't work for another**. This is why QMD provides the `profile-embeddings` command to measure the actual distribution on your corpus.

---

## How Hybrid Search Combines Results

The `query` command uses **Reciprocal Rank Fusion (RRF)** to merge results from keyword and vector search into a single ranking, then optionally refines it with an LLM reranker.

### RRF fusion

RRF was introduced by Cormack, Clarke & Buettcher ([SIGIR 2009](https://cormack.uwaterloo.ca/cormacksigir09-rrf.pdf)). It combines ranked lists using only rank positions, ignoring raw scores entirely:

```
contribution = weight / (k + rank + 1)
```

- **k = 60** (the constant from the original paper)
- **rank** is 0-indexed (rank 0 = top result)
- **weight** = 2.0 for the first two ranked lists (original query results), 1.0 for the rest (expanded query variants)

A small **top-rank bonus** is added: +0.05 for the rank-1 result, +0.02 for ranks 2-3. Documents appearing in multiple lists accumulate contributions from each.

### Score blending with the reranker

After RRF produces a ranking, the top candidates are re-scored by an LLM reranker (Qwen3-Reranker). The final score blends the RRF position with the reranker's relevance judgment:

```
rrfWeight = 0.75 (ranks 1-3) | 0.60 (ranks 4-10) | 0.40 (ranks 11+)
finalScore = rrfWeight * (1 / rrfRank) + (1 - rrfWeight) * rerankScore
```

Top-ranked results lean more on their RRF position (75%), while lower-ranked results lean more on the reranker's opinion (60%).

### The single-source problem

RRF works best when multiple independent sources contribute results. When BM25 returns nothing (the query terms don't appear in any document), only vector search contributes. In this case:

- Documents receive RRF scores from vector backends alone, with no cross-system agreement to validate them.
- The top-ranked result gets `1/(k+1) = 1/61 ≈ 0.016` from RRF plus the top-rank bonus, then gets blended at 75% positional weight — producing a high-looking score regardless of actual relevance.
- The reranker at 25% weight for top results cannot veto an irrelevant top-ranked document.

This is a known limitation of RRF. As Turnbull notes: "RRF'ing bad search into good search will just drag down the good search" ([blog](https://softwaredoug.com/blog/2024/11/03/rrf-is-not-enough)). Documents missing from a ranking contribute zero to that ranking's sum, so there is no penalty for lacking agreement across sources ([Elasticsearch docs](https://www.elastic.co/docs/reference/elasticsearch/rest-apis/reciprocal-rank-fusion)).

---

## Safeguards

QMD applies several layers of filtering to mitigate false positives:

1. **Adaptive FTS gate** (`FtsMinSignal`, default 0.3) — After the BM25 probe, if the best normalized BM25 score (`|bm25|/(1+|bm25|)`, mapped to [0,1)) is below this threshold, the FTS ranked lists are excluded from RRF fusion entirely. This prevents low-quality keyword matches from diluting the vector signal. When FTS lists are excluded, the positional weight rule (`i < 2 → weight 2.0`) shifts from FTS noise to the first two vector lists, amplifying the stronger signal. The pipeline degrades gracefully to vector-only fusion.

2. **Vector-score gate** (`VecOnlyGateThreshold`, default 0.25) — When BM25 finds no matches (or was gated out) and all vector similarity scores are below this threshold, the pipeline returns empty results before fusion runs. The default of 0.25 is calibrated for Qwen3-Embedding's similarity scale (median ~0.32).

3. **Reranker gate** (`RerankGateThreshold`, default 0.05) — After reranking, if BM25 found nothing and the best reranker score is below this value, results are discarded. The reranker outputs calibrated probabilities where scores near zero indicate the model considers the document irrelevant.

4. **Confidence gap filter** (`ConfidenceGapRatio`, default 0.5) — Results scoring below 50% of the top result's blended score are dropped. This separates a confident cluster of results from trailing noise.

5. **Raised defaults** — `vsearch --min-score` defaults to 0.5, and `query --min-score` defaults to 0.2. Both can be overridden on the command line.

The four threshold values (safeguards 1–4) are model- and corpus-dependent. They can be automatically calibrated using `qmd autotune` and are persisted in the database. Use `qmd status` to see active values.

---

## Calibrating Thresholds

The default thresholds are conservative starting points tuned for Qwen3-Embedding-0.6B. For optimal results:

- **Quick calibration:** Run `qmd autotune` to derive `VecOnlyGateThreshold` from your corpus's embedding similarity profile.
- **Thorough calibration:** Run `qmd autotune --fixture <fixture.json>` to grid-search `FtsMinSignal` and `ConfidenceGapRatio` against a benchmark fixture and find the combination that maximizes hybrid F1.
- **Manual inspection:** Run `qmd profile-embeddings` to see the raw similarity distribution. See [Calibrating Search Thresholds](profile-embeddings.md) for a full guide.

Autotuned thresholds are saved to the database and loaded automatically. Use `qmd autotune --reset` to revert to defaults.

---

## Measuring Search Quality with `bench`

The benchmark command runs your queries through all four search backends and measures retrieval quality:

```
qmd bench <fixture.json> [--json] [--collection C]
```

### Creating a fixture file

A fixture is a JSON file with queries and their expected results:

```json
{
  "description": "My corpus benchmark",
  "version": 1,
  "collection": "my-docs",
  "queries": [
    {
      "id": "exact-api",
      "query": "API versioning",
      "type": "exact",
      "description": "Direct keyword match",
      "expected_files": ["api-design-principles.md"],
      "expected_in_top_k": 1
    },
    {
      "id": "semantic-rest",
      "query": "how to structure REST endpoints",
      "type": "semantic",
      "description": "Conceptual match, no exact keyword overlap",
      "expected_files": ["api-design-principles.md"],
      "expected_in_top_k": 3
    },
    {
      "id": "alias-remote",
      "query": "working from home guidelines",
      "type": "alias",
      "description": "Synonym match for remote work policy",
      "expected_files": ["remote-work-policy.md"],
      "expected_in_top_k": 3
    }
  ]
}
```

- **`expected_files`**: File paths that should appear in results.
- **`expected_in_top_k`**: How many top results to check. Use 1 for exact-match queries, 3-5 for semantic ones.
- **`type`**: Freeform label for grouping (e.g., "exact", "semantic", "alias"). Not used by the scorer.

### Backends tested

| Backend | Description |
|---------|-------------|
| `bm25` | Keyword search only |
| `vector` | Vector search with query expansion |
| `hybrid` | RRF fusion without reranking |
| `full` | RRF fusion with LLM reranking |

### Metrics

- **Precision@k**: What fraction of the top-k results are relevant? `hits_in_top_k / min(k, expected_count)`.
- **Recall**: What fraction of expected files appeared anywhere in results? `total_hits / expected_count`.
- **MRR** (Mean Reciprocal Rank): How high did the first relevant result rank? `1 / rank_of_first_hit` (0 if not found).
- **F1**: Harmonic mean of precision and recall. Balances both.
- **Latency**: Wall-clock time in milliseconds.

### Tips for writing good test queries

- Include a mix of exact-match queries (where BM25 should excel), semantic queries (where vector search adds value), and synonym/alias queries.
- Include 1-2 negative queries — topics not in your corpus — to verify that safeguards return empty results.
- Start with 5-10 queries. Even a small set reveals whether keyword or semantic search is pulling its weight for your specific documents.
- Set `expected_in_top_k` realistically: 1 for obvious keyword matches, 3-5 for conceptual queries.

---

## References

### Academic Papers

1. Cormack, G. V., Clarke, C. L., & Buettcher, S. (2009). "Reciprocal rank fusion outperforms Condorcet and individual rank learning methods." SIGIR 2009. [[paper](https://cormack.uwaterloo.ca/cormacksigir09-rrf.pdf)]
2. Ethayarajh, K. (2019). "How Contextual are Contextualized Word Representations?" EMNLP 2019. [[ACL Anthology](https://aclanthology.org/D19-1006/)]
3. Su, J. et al. (2021). "Learning to Remove: Towards Isotropic Pre-trained BERT Embedding." [[arXiv 2104.05274](https://ar5iv.labs.arxiv.org/html/2104.05274)]
4. Bruch, S., Gai, S., & Ingber, A. (2023). "An Analysis of Fusion Functions for Hybrid Retrieval." ACM TOIS. [[arXiv 2210.11934](https://arxiv.org/abs/2210.11934)]
5. Steck, H. et al. (2024). "Is Cosine-Similarity of Embeddings Really About Similarity?" Netflix Research. [[arXiv 2403.05440](https://arxiv.org/html/2403.05440v1)]
6. Godey, N. et al. (2024). "Anisotropy Is Inherent to Self-Attention in Transformers." ICLR 2024. [[arXiv 2401.12143](https://arxiv.org/html/2401.12143v2)]
7. Benham, R. & Culpepper, J.S. (2017). "Risk-Reward Trade-offs in Rank Fusion." ADCS 2017. [[paper](https://rodgerbenham.github.io/bc17-adcs.pdf)]

### Practitioner Guides

8. Turnbull, D. (2024). "RRF is Not Enough." [[blog](https://softwaredoug.com/blog/2024/11/03/rrf-is-not-enough)]
9. Turnbull, D. (2025). "Elasticsearch Hybrid Search Recipes — Benchmarked." [[blog](https://softwaredoug.com/blog/2025/03/13/elasticsearch-hybrid-search-strategies)]
10. Berens, N. "Understanding RAG Score Thresholds." [[blog](https://nickberens.me/blog/understanding-rag-score-thresholds/)]
11. Dodds, K.C. "Implementing Hybrid Semantic + Lexical Search." [[blog](https://kentcdodds.com/blog/implementing-hybrid-semantic-lexical-search)]
12. Cohere. "Best Practices for Using Rerank." [[docs](https://docs.cohere.com/docs/reranking-best-practices)]

### Technical References

13. Elasticsearch. "Reciprocal Rank Fusion." [[docs](https://www.elastic.co/docs/reference/elasticsearch/rest-apis/reciprocal-rank-fusion)]
14. sqlite-vec. "API Reference." [[docs](https://alexgarcia.xyz/sqlite-vec/api-reference.html)]
15. Qwen/Qwen3-Reranker-0.6B. [[Hugging Face](https://huggingface.co/Qwen/Qwen3-Reranker-0.6B)]
