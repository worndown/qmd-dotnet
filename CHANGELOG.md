# Changelog

## [Unreleased]

### Changes

- Mitigate vector search false positives when BM25 returns no matches:
  vector-score gate (cosine < 0.55), reranker gate (score < 0.1), score cap
  at best raw vector similarity, and post-fusion confidence gap filter (50%).
  Raise default `--min-score` for `vsearch` from 0.3 to 0.5 and `query`
  from 0.0 to 0.2. Emit stderr warning when results are semantic-only.
- New `qmd profile-embeddings` command and SDK `ProfileEmbeddingsAsync()`
  method to measure embedding model similarity distribution on indexed corpus.
  Reports percentile statistics and suggests a `--min-score` threshold
  calibrated to the specific model + corpus.
- Update README on RRF limitations.

## [1.0.0] - 2024-04-10

Initial .NET port of **qmd v2.1.0**.

### Changes

- Ported TypeScript to C#
- New repository and project scaffolding