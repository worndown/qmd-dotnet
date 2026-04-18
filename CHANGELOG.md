# Changelog

## [Unreleased]

## [1.3.0] - 2026-04-18

### New features

- `qmd autotune` command: tunes search thresholds automatically, either by deriving them from the embedding similarity distribution (profile-only mode) or by running a grid search over `FtsMinSignal` × `ConfidenceGapRatio`. Configuration is persisted per-index as `SearchConfig` in the database. `qmd status` now shows the active config.
- Ctrl+C support: `Console.CancelKeyPress` is hooked on startup and propagates cancellation cleanly through all store and reindex operations.

### Search

- `--min-score` now relaxes internal gate thresholds (`FtsMinSignal`, `VecOnlyGateThreshold`, `RerankGateThreshold`) when set below their configured defaults, so callers can explicitly trade precision for recall. Relaxed gates are reported in `HybridQueryDiagnostics` and surfaced as a CLI warning.
- `FtsMinSignal` default lowered 0.30 -> 0.15; `vsearch --min-score` default lowered 0.5 -> 0.3.
- Vector search now merges intent-expansion and baseline-expansion candidates, so `--intent` can only improve recall, not reduce it.
- Removed the vec-only score cap to reduce false negatives.

### Models

- Default embed and rerank models switched to f16 GGUF variants (`worndown/Qwen3-Embedding-0.6B-GGUF` and `worndown/Qwen3-Reranker-0.6B-GGUF`).
- Default generate (query expansion) model switched to f16 quantization.
- Intent sampling set to greedy (Temperature=0, TopK/TopP removed) for deterministic query expansion output.

### SDK / public API

- All `IQmdStore` methods now accept a `CancellationToken`.
- `LlmServiceFactory` now exposes `ResolveEmbedModel()`, `ResolveRerankModel()`, and `ResolveGenerateModel()` — unifying the config-override -> env-var -> default fallback chain used by all callers.

### Internal

- Skill content (`qmd_skill.md`, `mcp_setup.md`) moved from hardcoded base64 strings in `EmbeddedSkills.cs` to embedded resource files. `SkillInstaller.Install()` now returns `SkillInstallResult` with a `SymlinkOutcome` enum, including a new `ClaudeNotDetected` case.
- `CharBasedTokenizer` moved to the test project; `AvgCharsPerToken` extracted to `ChunkConstants` so `DocumentChunker` and tests share the same value; batch limits in `BatchAssembler` and `EmbedPipelineOptions` now reference `LlmConstants` instead of duplicating magic numbers.
- Deterministic builds enabled.

### Tests

- LLM tests serialized via `LlmEnvironmentCollection` to prevent resource contention across `EmbeddingFormatterTests`, `LlmConstantsTests`, and `LlamaSharpIntegrationTests`; embedding dimension assertion relaxed to a sane range.
- Eval corpus expanded: 14 corpus documents and 45 queries added.

### Docs

- Added `docs/custom-models.md`: `QMD_*` environment variables reference, `hf:` URI format, step-by-step Hugging Face URL conversion, local file paths, model cache location, and caveats (re-embedding, prompt format detection, context size).

## [1.2.0] - 2026-04-14

### New features

- Added support for C/C++ and C# languages for document indexing.

### SDK / public API

- Replace `Action<T>` progress callbacks with `IProgress<T>`, aligning the API with standard .NET conventions and enabling proper synchronization context handling (#22).
- CLI console I/O abstraction: `Console.Write`/`Console.Error.WriteLine` abstracted behind `IConsoleOutput` interface for testable CLI output (#23).

### Internal

- Typed database row models: replaced dynamic `GetDynamic()`/`AllDynamic()` calls with strongly-typed `Get<T>()`/`All<T>()` using internal record/class types for every SQL query result shape, eliminating brittle dictionary access and most null-forgiving operator usage (#17).
- Error handling: replaced silent `catch { }` blocks with typed exception handlers, introduced domain-specific exception types, and ensured `CancellationToken`/`OperationCanceledException` is never swallowed (#19).
- Separate pure utility classes from stateful services that depend on database/I/O; extract inline record definitions to their own files (#20).
- Convert stateful static services to instance classes with interfaces for proper dependency injection and testability (#21).
- Pass `CancellationToken` through to all CLI commands.
- Removed duplicate code and renamed classes for clarity.

### Tests

- Added `[Trait]` categorization to all test classes, extracted repeated setup into shared helpers, and established conventions for filtered test runs.

### Build

- Added missing NuGet package properties (#16).

## [1.1.0] - 2026-04-13

### Search

- Mitigate vector search false positives when BM25 returns no matches: vector-score gate (cosine < 0.55), reranker gate (score < 0.1), score cap at best raw vector similarity, and post-fusion confidence gap filter (50%).
- Raise default `--min-score` for `vsearch` from 0.3 -> 0.5 and `query` from 0.0 -> 0.2. Emit a stderr warning when results are semantic-only.

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

## [1.0.0] - 2026-04-10

Initial .NET port of **qmd v2.1.0**.

### Changes

- Ported TypeScript to C#
- New repository and project scaffolding