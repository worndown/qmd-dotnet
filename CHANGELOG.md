# Changelog

## [Unreleased]

### Search

- Mitigate vector search false positives when BM25 returns no matches: vector-score gate (cosine < 0.55), reranker gate (score < 0.1), score cap at best raw vector similarity, and post-fusion confidence gap filter (50%).
- Raise default `--min-score` for `vsearch` from 0.3 → 0.5 and `query` from 0.0 → 0.2. Emit a stderr warning when results are semantic-only.

### New features

- `qmd profile-embeddings` command and `IQmdStore.ProfileEmbeddingsAsync()` — measures embedding similarity distribution on the indexed corpus and suggests a calibrated `--min-score` threshold.
- `IQmdStore.CleanupAsync(CleanupOptions?)` — single API call for all database maintenance: cache eviction, inactive-document removal, orphan cleanup, and VACUUM.
- `LlmServiceFactory` — public factory for creating `ILlmService` instances and resolving model files, replacing direct use of internal types.

### SDK / public API

- `SnippetExtractor` is now public; SDK consumers can use it to extract relevant text snippets from document bodies.
- Removed `InternalsVisibleTo("qmd")` from `Qmd.Core` — `Qmd.Cli` now depends only on the public SDK surface.

### Internal

- Merged separate `Qmd.Sdk`, `Qmd.Llm`, and `Qmd.Mcp` projects into `Qmd.Core`.
- Eliminated `QmdStoreImpl` adapter; `QmdStore` now implements `IQmdStore` directly.
- Moved output formatters (`DocumentFormatter`, `SearchResultFormatter`, etc.) and skills installer (`SkillInstaller`) from `Qmd.Core` to `Qmd.Cli`.

### Docs

- Added Hybrid Search Guide (`docs/hybrid-search-guide.md`).
- Added eval corpus under `evals/`.

## [1.0.0] - 2024-04-10

Initial .NET port of **qmd v2.1.0**.

### Changes

- Ported TypeScript to C#
- New repository and project scaffolding