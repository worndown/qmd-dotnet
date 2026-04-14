# Changelog

## [Unreleased]

## [v1.2.0] - 2026-04-14

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

## [1.0.0] - 2026-04-10

Initial .NET port of **qmd v2.1.0**.

### Changes

- Ported TypeScript to C#
- New repository and project scaffolding