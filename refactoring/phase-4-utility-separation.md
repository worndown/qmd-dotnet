# Phase 4: Separate Pure Utilities from Stateful Services

## Goal

Clearly delineate which static classes are pure functions (and should remain static -- this is idiomatic C#) versus which are stateful services that depend on database/I/O (and will be converted to instance classes in Phase 5). Also extract inline record definitions to their own files and move misplaced types.

## Why This Matters

The codebase has ~35 static classes. Some are genuinely pure (no side effects, no I/O, no database) and should stay static -- this is normal in C# (e.g., `Math`, `Path`, `Convert`). Others take `IQmdDatabase` or `QmdStore` as parameters and are really services masquerading as static functions. Phase 5 will convert only the service-like statics, but first we need a clean separation.

Additionally, several record types are defined inline at the bottom of unrelated files (a pattern common in TypeScript but unusual in C#).

## Detailed Changes

### Step 1: Document Pure Utility Classification

These static classes are **pure functions** and will **remain static** (no changes needed other than documentation):

| Class | File | Reason |
|-------|------|--------|
| `ContentHasher.HashContent` | `Content/ContentHasher.cs` | `string -> string`, SHA256 |
| `TitleExtractor` | `Content/TitleExtractor.cs` | `string, string -> string` |
| `TextUtils` | `Content/TextUtils.cs` | `string -> string` |
| `DocidUtils` | `Paths/DocidUtils.cs` | `string -> string` |
| `Handelize` | `Paths/Handelize.cs` | `string -> string` |
| `FtsUtils` | `Paths/FtsUtils.cs` | `string -> string` |
| `VirtualPaths` | `Paths/VirtualPaths.cs` | `string -> VirtualPath?` |
| `QmdPaths` | `Paths/QmdPaths.cs` | Path resolution utilities |
| `Fts5QueryBuilder` | `Search/Fts5QueryBuilder.cs` | `string -> string?` |
| `QueryValidator` | `Search/QueryValidator.cs` | `string -> string?` |
| `RrfFusion` | `Search/RrfFusion.cs` | `lists -> list` |
| `IntentProcessor` | `Snippets/IntentProcessor.cs` | `string -> List<string>` |
| `EmbeddingFormatter` | `Embedding/EmbeddingFormatter.cs` | `string -> string` |
| `BatchAssembler` | `Embedding/BatchAssembler.cs` | `list -> list of lists` |
| `SnippetExtractor` | `Snippets/SnippetExtractor.cs` | `string, string -> SnippetResult` |
| `SearchConstants` | `Search/FtsSearcher.cs` | Constants only |
| `ChunkConstants` | `Chunking/ChunkConstants.cs` | Constants only |
| `LlmConstants` | `Llm/LlmConstants.cs` | Constants only |

These static classes are **services** and will be **converted to instance classes** in Phase 5:

| Class | File | Dependency |
|-------|------|------------|
| `HybridQueryService` | `Search/HybridQueryService.cs` | `QmdStore`, `ILlmService` |
| `StructuredSearchService` | `Search/StructuredSearchService.cs` | `QmdStore`, `ILlmService` |
| `VectorSearchQueryService` | `Search/VectorSearchQueryService.cs` | `QmdStore`, `ILlmService` |
| `FtsSearcher` | `Search/FtsSearcher.cs` | `IQmdDatabase` |
| `VectorSearcher` | `Search/VectorSearcher.cs` | `IQmdDatabase`, `ILlmService` |
| `QueryExpander` | `Search/QueryExpander.cs` | `IQmdDatabase`, `ILlmService` |
| `Reranker` | `Search/Reranker.cs` | `IQmdDatabase`, `ILlmService` |
| `EmbeddingProfiler` | `Search/EmbeddingProfiler.cs` | `IQmdDatabase`, `ILlmService` |
| `DocumentFinder` | `Retrieval/DocumentFinder.cs` | `IQmdDatabase` |
| `MultiGetService` | `Retrieval/MultiGetService.cs` | `IQmdDatabase` |
| `ContextResolver` | `Retrieval/ContextResolver.cs` | `IQmdDatabase` |
| `FuzzyMatcher` | `Retrieval/FuzzyMatcher.cs` | `IQmdDatabase` |
| `GlobMatcher` | `Retrieval/GlobMatcher.cs` | `IQmdDatabase` |
| `DocumentOperations` | `Documents/DocumentOperations.cs` | `IQmdDatabase` |
| `EmbeddingPipeline` | `Embedding/EmbeddingPipeline.cs` | `IQmdDatabase`, `ILlmService` |
| `EmbeddingOperations` | `Embedding/EmbeddingOperations.cs` | `IQmdDatabase` |
| `CollectionReindexer` | `Indexing/CollectionReindexer.cs` | `QmdStore` |
| `MaintenanceOperations` | `Indexing/MaintenanceOperations.cs` | `IQmdDatabase` |
| `StatusOperations` | `Indexing/StatusOperations.cs` | `IQmdDatabase` |
| `CacheOperations` | `Indexing/CacheOperations.cs` | `IQmdDatabase` |
| `ConfigSync` | `Configuration/ConfigSync.cs` | `IQmdDatabase` |

### Step 2: Split ContentHasher

`ContentHasher` currently mixes a pure function (`HashContent`) with a database operation (`InsertContent`). Split them:

**Keep in `ContentHasher`** (pure):
- `HashContent(string content) -> string`

**Move to `DocumentOperations`** (or keep in ContentHasher but mark as service-bound):
- `InsertContent(IQmdDatabase db, string hash, string content, string createdAt)`

Since `InsertContent` is called from `CollectionReindexer` and `QmdStore`, and both already access the database, this is a clean split. In Phase 5, `InsertContent` will become part of a repository service.

For now in Phase 4: add a comment marking `InsertContent` as a database operation that will move in Phase 5. Do not move it yet to avoid unnecessary test churn.

### Step 3: Extract Inline Record Definitions

Move these records from the end of their host files to their own files:

| Record | Current Location | New Location |
|--------|-----------------|--------------|
| `SearchConstants` | `src/Qmd.Core/Search/FtsSearcher.cs:84` | `src/Qmd.Core/Search/SearchConstants.cs` |
| `ReindexOptions` | `src/Qmd.Core/Indexing/CollectionReindexer.cs:12` | `src/Qmd.Core/Indexing/ReindexOptions.cs` |
| `ActiveDocumentRow` | `src/Qmd.Core/Documents/ActiveDocumentRow.cs` (check if it's in DocumentOperations) | Own file if co-located |
| `ParsedStructuredQuery` | `src/Qmd.Cli/Commands/CliHelper.cs:192` | `src/Qmd.Cli/Commands/ParsedStructuredQuery.cs` |
| `RankedListMeta` | Check if inline in HybridQueryService or HybridTypes | Own file or add to HybridTypes.cs |

**Note**: Before extracting, verify each record's current location by reading the file. Some may already be in their own files.

### Step 4: Move Options Types to Models

Types that are part of the public/internal API surface but are defined inside service files should move to `Models/`:

| Type | Current Location | New Location |
|------|-----------------|--------------|
| `VectorSearchQueryOptions` | `Search/VectorSearchQueryService.cs` | `Models/SearchTypes.cs` (or keep in file if internal-only) |
| `EmbeddingProfileOptions`, `EmbeddingProfile` | `Search/EmbeddingProfiler.cs` | `Models/EmbeddingTypes.cs` (only if public) |

**Rule**: Only move types if they're referenced from multiple files or are part of the public API. Internal-only types used by a single service can stay co-located.

## Files Modified

| File | Change Type |
|------|-------------|
| `src/Qmd.Core/Search/SearchConstants.cs` | **NEW** -- extracted from FtsSearcher.cs |
| `src/Qmd.Core/Search/FtsSearcher.cs` | Remove SearchConstants (moved out) |
| `src/Qmd.Core/Indexing/ReindexOptions.cs` | **NEW** -- extracted from CollectionReindexer.cs |
| `src/Qmd.Core/Indexing/CollectionReindexer.cs` | Remove ReindexOptions (moved out) |
| `src/Qmd.Cli/Commands/ParsedStructuredQuery.cs` | **NEW** -- extracted from CliHelper.cs |
| `src/Qmd.Cli/Commands/CliHelper.cs` | Remove ParsedStructuredQuery (moved out) |
| Other inline types as discovered | File splits |

## Test Strategy

No new tests needed. Existing tests should pass unchanged since:
- Namespaces don't change (records stay in the same namespace)
- Only file boundaries change, not API

Run full test suite to verify: `dotnet test Qmd.slnx -c Release --filter "Category!=LLM"`

## Risk Assessment

**LOW**. File moves and record extraction are mechanical. No behavior changes. The only risk is a missed reference, which the compiler will catch.

## Dependencies

Should come after Phase 1 (typed row models) so the new `RowModels.cs` file is in place. Can run in parallel with Phase 2 and Phase 3.
