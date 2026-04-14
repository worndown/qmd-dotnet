# Phase 1: Typed Database Row Models

## Goal

Replace all `GetDynamic()` / `AllDynamic()` calls with strongly-typed `Get<T>()` / `All<T>()` calls by introducing internal record/class types for every distinct SQL query result shape. This eliminates brittle `row["column"]!.ToString()!` dictionary access, provides compile-time type safety, and removes the majority of null-forgiving (`!`) operator usage in the codebase.

## Why This Matters

The current codebase maps SQL results to `Dictionary<string, object?>` and accesses columns by string key with aggressive null-forgiving assertions. This pattern:
- Bypasses the type system entirely (no compile-time checks on column names or types)
- Is brittle: a renamed/removed SQL column causes a silent runtime `NullReferenceException`
- Produces noisy code with chains of `row["col"]!.ToString()!`
- Was inherited from the TypeScript port where dynamic object access is natural

The `SqliteStatement` class already has a working `MapRow<T>()` method (reflection-based, with snake_case-to-PascalCase conversion) used by `Get<T>()` and `All<T>()`. This phase simply routes all callers through it.

## Detailed Changes

### Step 1: Create Row Model Types

Create `src/Qmd.Core/Database/RowModels.cs` containing internal record/class types for every distinct query shape. Group them logically:

```csharp
namespace Qmd.Core.Database;

// -- Document queries (DocumentFinder, QmdStore.ListFilesAsync) --

internal class DocumentRow
{
    public string VirtualPath { get; set; } = "";
    public string DisplayPath { get; set; } = "";
    public string Title { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Collection { get; set; } = "";
    public string ModifiedAt { get; set; } = "";
    public int BodyLength { get; set; }
    public string? Body { get; set; }
}

internal class DocidRow
{
    public string Filepath { get; set; } = "";
    public string Hash { get; set; } = "";
}

internal class BodyRow
{
    public string? Body { get; set; }
}

internal class ListFileRow
{
    public string Path { get; set; } = "";
    public int Size { get; set; }
}

// -- Collection queries (ContextResolver, ConfigSync, StatusOperations) --

internal class StoreCollectionRow
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public string? Context { get; set; }
}

internal class CollectionDocCountRow
{
    public string Collection { get; set; } = "";
    public int DocCount { get; set; }
}

// -- Search queries (FtsSearcher, VectorSearcher) --

internal class FtsMatchRow
{
    public string Filepath { get; set; } = "";
    public string DisplayPath { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string Hash { get; set; } = "";
    public string Collection { get; set; } = "";
    public double Bm25Score { get; set; }
}

internal class VectorMatchRow
{
    public string HashSeq { get; set; } = "";
    public double Distance { get; set; }
}

internal class ContentVectorDocRow
{
    public string HashSeq { get; set; } = "";
    public string Hash { get; set; } = "";
    public int Pos { get; set; }
    public string Filepath { get; set; } = "";
    public string DisplayPath { get; set; } = "";
    public string Title { get; set; } = "";
    public string Collection { get; set; } = "";
    public string? Body { get; set; }
}

// -- Simple/utility rows --

internal class CountRow
{
    public int Cnt { get; set; }
}

internal class SingleValueRow
{
    public string? Value { get; set; }
}

internal class SingleNameRow
{
    public string Name { get; set; } = "";
}

internal class SinglePathRow
{
    public string Path { get; set; } = "";
}

internal class SqliteMasterRow
{
    public string Name { get; set; } = "";
}

// -- Embedding queries (EmbeddingOperations) --

internal class PendingEmbeddingRow
{
    public string Hash { get; set; } = "";
    public int Bytes { get; set; }
}

internal class EmbeddingDocRow
{
    public string Hash { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Body { get; set; }
    public int Bytes { get; set; }
}

// -- Fuzzy matching --

internal class FuzzyPathRow
{
    public string Path { get; set; } = "";
}
```

**Note**: The exact set of row types should be determined by auditing every `GetDynamic`/`AllDynamic` call site. The above is representative; some queries may share a row type, others may need unique ones. Pay attention to SQL column aliases -- the `MapRow<T>` method uses `ToPascalCase` on the SQL column name to find the C# property.

### Step 2: Migrate Each Call Site

For each file below, replace `GetDynamic()`/`AllDynamic()` with `Get<T>()`/`All<T>()` and replace dictionary access with property access.

#### `src/Qmd.Core/Retrieval/DocumentFinder.cs` (heaviest user)

**FindDocument method** (lines 45-53, 58-67, 87-95): Three queries all return the same shape. Replace:
```csharp
// Before:
var doc = db.Prepare("SELECT ... as virtual_path, ...").GetDynamic(filepath);
// ...
var hash = doc["hash"]!.ToString()!;
var virtualPath = doc["virtual_path"]!.ToString()!;
```
With:
```csharp
// After:
var doc = db.Prepare("SELECT ... as virtual_path, ...").Get<DocumentRow>(filepath);
// ...
var hash = doc.Hash;
var virtualPath = doc.VirtualPath;
```

**FindDocument collection lookup** (line 73): `AllDynamic()` for store_collections:
```csharp
// Before:
var collections = db.Prepare("SELECT name, path FROM store_collections").AllDynamic();
foreach (var coll in collections)
{
    var collName = coll["name"]!.ToString()!;
    var collPath = coll["path"]?.ToString() ?? "";
```
With:
```csharp
// After:
var collections = db.Prepare("SELECT name, path FROM store_collections").All<StoreCollectionRow>();
foreach (var coll in collections)
{
    var collName = coll.Name;
    var collPath = coll.Path ?? "";
```

**FindDocumentByDocid** (lines 184-189): Replace with `Get<DocidRow>()`.

**GetDocumentBody** (lines 131-177): Replace `GetDynamic()` calls with `Get<BodyRow>()` and `AllDynamic()` collection queries with `All<StoreCollectionRow>()`.

#### `src/Qmd.Core/Search/FtsSearcher.cs` (line 55)

Replace `AllDynamic()` with `All<FtsMatchRow>()`. Replace `row["bm25_score"]`, `row["hash"]!.ToString()!`, etc. with `row.Bm25Score`, `row.Hash`, etc.

**Important**: The FTS query uses dynamic SQL with optional collection filtering and variable parameter counts. The `All<T>()` call accepts `params object?[]` just like `AllDynamic()`, so the migration is drop-in.

#### `src/Qmd.Core/Search/VectorSearcher.cs`

Replace vector match query and content-vector join query with typed rows.

#### `src/Qmd.Core/Retrieval/ContextResolver.cs`

Replace collection and config queries with `All<StoreCollectionRow>()` and `Get<SingleValueRow>()`.

#### `src/Qmd.Core/Retrieval/FuzzyMatcher.cs` (line 42)

Replace `AllDynamic()` with `All<FuzzyPathRow>()`.

#### `src/Qmd.Core/Retrieval/GlobMatcher.cs`

Replace glob match query with typed row.

#### `src/Qmd.Core/Retrieval/MultiGetService.cs`

Replace document queries with typed rows.

#### `src/Qmd.Core/Store/QmdStore.cs` (ListFilesAsync, lines 248-280)

Replace `AllDynamic()` with `All<ListFileRow>()`. Replace `row["path"]!.ToString()!` with `row.Path`.

#### `src/Qmd.Core/Embedding/EmbeddingOperations.cs`

Replace pending-doc and embedding-doc queries with typed rows.

#### `src/Qmd.Core/Indexing/StatusOperations.cs`

Replace count and collection queries with typed rows.

#### `src/Qmd.Core/Indexing/MaintenanceOperations.cs`

Replace count queries with `Get<CountRow>()`.

#### `src/Qmd.Core/Indexing/CacheOperations.cs`

Replace cache result queries with `Get<SingleValueRow>()`.

#### `src/Qmd.Core/Search/QueryExpander.cs`

Replace cache result query with `Get<SingleValueRow>()`.

#### `src/Qmd.Core/Search/Reranker.cs`

Replace cache result query with `Get<SingleValueRow>()`.

#### `src/Qmd.Core/Search/EmbeddingProfiler.cs`

Replace multiple query shapes with typed rows.

#### `src/Qmd.Core/Configuration/ConfigSync.cs`

Replace name queries with `All<SingleNameRow>()`.

#### `src/Qmd.Core/Search/HybridQueryService.cs` (line 84)

Replace `GetDynamic()` for sqlite_master check with `Get<SqliteMasterRow>()`.

#### `src/Qmd.Core/Search/StructuredSearchService.cs` (line 82)

Same sqlite_master check as HybridQueryService.

### Step 3: Do NOT Remove GetDynamic/AllDynamic from IStatement

Keep `GetDynamic()` and `AllDynamic()` on the `IStatement` interface. They may still be useful for ad-hoc queries in tests or future code. The goal is to eliminate their use in production code, not to remove the capability.

## Files Modified

| File | Change Type |
|------|-------------|
| `src/Qmd.Core/Database/RowModels.cs` | **NEW** -- all row model types |
| `src/Qmd.Core/Retrieval/DocumentFinder.cs` | Replace ~8 GetDynamic/AllDynamic calls |
| `src/Qmd.Core/Search/FtsSearcher.cs` | Replace 1 AllDynamic call |
| `src/Qmd.Core/Search/VectorSearcher.cs` | Replace ~2 calls |
| `src/Qmd.Core/Retrieval/ContextResolver.cs` | Replace ~4 calls |
| `src/Qmd.Core/Retrieval/FuzzyMatcher.cs` | Replace 1 call |
| `src/Qmd.Core/Retrieval/GlobMatcher.cs` | Replace ~1 call |
| `src/Qmd.Core/Retrieval/MultiGetService.cs` | Replace ~3 calls |
| `src/Qmd.Core/Store/QmdStore.cs` | Replace AllDynamic in ListFilesAsync |
| `src/Qmd.Core/Embedding/EmbeddingOperations.cs` | Replace ~3 calls |
| `src/Qmd.Core/Indexing/StatusOperations.cs` | Replace ~3 calls |
| `src/Qmd.Core/Indexing/MaintenanceOperations.cs` | Replace ~2 calls |
| `src/Qmd.Core/Indexing/CacheOperations.cs` | Replace ~2 calls |
| `src/Qmd.Core/Search/QueryExpander.cs` | Replace 1 call |
| `src/Qmd.Core/Search/Reranker.cs` | Replace 1 call |
| `src/Qmd.Core/Search/EmbeddingProfiler.cs` | Replace ~3 calls |
| `src/Qmd.Core/Configuration/ConfigSync.cs` | Replace ~1 call |
| `src/Qmd.Core/Search/HybridQueryService.cs` | Replace 1 GetDynamic call |
| `src/Qmd.Core/Search/StructuredSearchService.cs` | Replace 1 GetDynamic call |

## Test Strategy

### Existing tests should pass unchanged

All test method signatures remain identical. Tests call static methods that now internally use `Get<T>()` instead of `GetDynamic()` -- from the outside, nothing changes.

### New tests to add

Create `tests/Qmd.Core.Tests/Database/RowModelMappingTests.cs`:
- Seed a minimal in-memory database with known data
- Run each query shape and verify the typed row maps correctly
- This catches column-name mismatches between SQL aliases and C# property names (the most likely regression)

Example:
```csharp
[Fact]
public void DocumentRow_MapsFromDocumentQuery()
{
    // Insert a document + content
    // Run the same SELECT used in DocumentFinder
    // Verify all properties map correctly
}
```

## Risk Assessment

**LOW**. This is a mechanical find-and-replace per call site. The `MapRow<T>` reflection-based mapper already exists and is tested. The only risk is column name mismatches between SQL aliases and C# property names, which the new `RowModelMappingTests` will catch.

## Verification

1. `dotnet build Qmd.slnx -c Release` -- must compile cleanly
2. `dotnet test Qmd.slnx -c Release --filter "Category!=LLM"` -- all tests pass
3. Grep for remaining `GetDynamic`/`AllDynamic` in `src/` -- should be zero (excluding the interface/implementation definition)
