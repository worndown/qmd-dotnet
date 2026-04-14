# Phase 2: Test Infrastructure Improvements

## Goal

Add `[Trait]` categorization to all test classes, extract repeated test setup code into shared helpers, and establish conventions for test organization. This enables filtered test runs (e.g., run only unit tests, skip database tests) and reduces boilerplate across the ~58 test files.

## Why This Matters

Currently, only `LlamaSharpIntegrationTests` has a `[Trait("Category", "LLM")]` attribute. All other tests lack categorization, making it impossible to run subsets. The CI pipeline filters by `Category!=LLM`, but no other categories exist. Additionally, ~20 test classes repeat the same database setup boilerplate:

```csharp
private readonly IQmdDatabase _db;
public SomeTests()
{
    _db = new SqliteDatabase(":memory:");
    SchemaInitializer.Initialize(_db);
    VecExtension.TryLoad(_db);
}
public void Dispose() => _db.Dispose();
```

## Detailed Changes

### Step 1: Define Trait Categories

Establish these standard categories:

| Category | Meaning | Example |
|----------|---------|---------|
| `Unit` | Pure logic, no I/O, no database | `RrfFusionTests`, `HandelizeTests`, `Fts5QueryBuilderTests` |
| `Database` | Uses in-memory SQLite | `FtsSearcherTests`, `DocumentFinderTests`, `EmbeddingPipelineTests` |
| `Integration` | Multiple subsystems or external resources | `CliIntegrationTests`, `McpHttpTests`, `QmdStoreSdkTests` |
| `LLM` | Requires LLM model download (already exists) | `LlamaSharpIntegrationTests` |

### Step 2: Add Traits to All Test Classes

Add `[Trait("Category", "...")]` to every test class. Classification:

**Unit tests** (add `[Trait("Category", "Unit")]`):
- `tests/Qmd.Core.Tests/Chunking/BreakPointScannerTests.cs`
- `tests/Qmd.Core.Tests/Chunking/AstBreakPointScannerTests.cs`
- `tests/Qmd.Core.Tests/Content/TitleExtractorTests.cs`
- `tests/Qmd.Core.Tests/Content/ContentHasherTests.cs` (if it only tests `HashContent` -- check for DB usage)
- `tests/Qmd.Core.Tests/Paths/HandelizeTests.cs`
- `tests/Qmd.Core.Tests/Paths/DocidUtilsTests.cs`
- `tests/Qmd.Core.Tests/Paths/FtsUtilsTests.cs`
- `tests/Qmd.Core.Tests/Paths/VirtualPathsTests.cs`
- `tests/Qmd.Core.Tests/Search/Fts5QueryBuilderTests.cs`
- `tests/Qmd.Core.Tests/Search/RrfFusionTests.cs`
- `tests/Qmd.Core.Tests/Search/QueryValidatorTests.cs`
- `tests/Qmd.Core.Tests/Snippets/IntentProcessorTests.cs`
- `tests/Qmd.Core.Tests/Snippets/SnippetExtractorTests.cs`
- `tests/Qmd.Core.Tests/Embedding/BatchAssemblerTests.cs`
- `tests/Qmd.Core.Tests/Llm/EmbeddingFormatterTests.cs`
- `tests/Qmd.Core.Tests/Bench/BenchmarkScorerTests.cs`
- `tests/Qmd.Cli.Tests/CliHelperTests.cs`
- `tests/Qmd.Cli.Tests/Formatting/FormatterTests.cs`
- `tests/Qmd.Cli.Tests/Progress/TerminalProgressTests.cs`
- `tests/Qmd.Cli.Tests/Skills/EmbeddedSkillsTests.cs`

**Database tests** (add `[Trait("Category", "Database")]`):
- `tests/Qmd.Core.Tests/Search/FtsSearcherTests.cs`
- `tests/Qmd.Core.Tests/Search/VectorSearcherTests.cs`
- `tests/Qmd.Core.Tests/Search/QueryExpanderTests.cs`
- `tests/Qmd.Core.Tests/Search/RerankerTests.cs`
- `tests/Qmd.Core.Tests/Search/HybridQueryTests.cs`
- `tests/Qmd.Core.Tests/Search/EmbeddingProfilerTests.cs`
- `tests/Qmd.Core.Tests/Retrieval/DocumentFinderTests.cs`
- `tests/Qmd.Core.Tests/Retrieval/ContextResolverTests.cs`
- `tests/Qmd.Core.Tests/Retrieval/FuzzyMatcherTests.cs`
- `tests/Qmd.Core.Tests/Retrieval/GlobMatcherTests.cs`
- `tests/Qmd.Core.Tests/Documents/DocumentOperationsTests.cs`
- `tests/Qmd.Core.Tests/Embedding/EmbeddingPipelineTests.cs`
- `tests/Qmd.Core.Tests/Embedding/EmbeddingOperationsTests.cs`
- `tests/Qmd.Core.Tests/Indexing/CacheOperationsTests.cs`
- `tests/Qmd.Core.Tests/Indexing/CollectionReindexerTests.cs`
- `tests/Qmd.Core.Tests/Indexing/StatusOperationsTests.cs`
- `tests/Qmd.Core.Tests/Configuration/ConfigManagerTests.cs`
- `tests/Qmd.Core.Tests/Configuration/ConfigSyncTests.cs`
- `tests/Qmd.Core.Tests/Database/SqliteDatabaseTests.cs`
- `tests/Qmd.Core.Tests/Database/SchemaInitializerTests.cs`
- `tests/Qmd.Core.Tests/Database/VecExtensionTests.cs`
- `tests/Qmd.Core.Tests/Chunking/DocumentChunkerTests.cs` (if it uses DB)
- `tests/Qmd.Core.Tests/Chunking/TokenBasedChunkingTests.cs` (if it uses DB)

**Integration tests** (add `[Trait("Category", "Integration")]`):
- `tests/Qmd.Core.Tests/Store/QmdStoreSdkTests.cs`
- `tests/Qmd.Core.Tests/Mcp/QmdToolsTests.cs`
- `tests/Qmd.Core.Tests/Mcp/QmdResourcesTests.cs`
- `tests/Qmd.Core.Tests/Mcp/McpHttpTests.cs`
- `tests/Qmd.Cli.Tests/CliIntegrationTests.cs`
- `tests/Qmd.Cli.Tests/Skills/SkillInstallerTests.cs` (if it touches filesystem)

**LLM tests** (already has `[Trait("Category", "LLM")]`):
- `tests/Qmd.Core.Tests/Llm/LlamaSharpIntegrationTests.cs`

**Note**: Before applying traits, read each test class to verify its actual dependencies. Some classes named `*Tests` may use in-memory DB but not be obvious from the name. The classification above is approximate -- verify during implementation.

### Step 3: Extract Shared Test Helpers

Create `tests/Qmd.Core.Tests/TestHelpers/TestDbHelper.cs`:

```csharp
namespace Qmd.Core.Tests.TestHelpers;

using Qmd.Core.Database;

internal static class TestDbHelper
{
    /// <summary>
    /// Create an initialized in-memory SQLite database with schema and vec extension.
    /// </summary>
    public static IQmdDatabase CreateInMemoryDb()
    {
        var db = new SqliteDatabase(":memory:");
        SchemaInitializer.Initialize(db);
        VecExtension.TryLoad(db);
        return db;
    }

    /// <summary>
    /// Insert a document with content into the database.
    /// </summary>
    public static void SeedDocument(IQmdDatabase db, string collection, string path,
        string content, string? title = null)
    {
        var hash = Content.ContentHasher.HashContent(content);
        var now = "2025-01-01T00:00:00Z";
        Content.ContentHasher.InsertContent(db, hash, content, now);
        title ??= Content.TitleExtractor.ExtractTitle(content, path);
        Documents.DocumentOperations.InsertDocument(db, collection, path, title, hash, now, now);
    }

    /// <summary>
    /// Insert a collection into store_collections.
    /// </summary>
    public static void SeedCollection(IQmdDatabase db, string name, string path,
        string? context = null)
    {
        db.Prepare("INSERT OR REPLACE INTO store_collections (name, path, context) VALUES ($1, $2, $3)")
            .Run(name, path, context ?? (object)DBNull.Value);
    }
}
```

### Step 4: Migrate Test Classes to Use Shared Helpers

Replace repeated setup patterns. For example, in test constructors:

```csharp
// Before:
public FtsSearcherTests()
{
    _db = new SqliteDatabase(":memory:");
    SchemaInitializer.Initialize(_db);
    VecExtension.TryLoad(_db);
    // seed documents...
}

// After:
public FtsSearcherTests()
{
    _db = TestDbHelper.CreateInMemoryDb();
    TestDbHelper.SeedDocument(_db, "docs", "readme.md", "# Hello World\nThis is a test.");
    // ...
}
```

Do this incrementally -- focus on the ~20 classes that have the full `new SqliteDatabase` + `SchemaInitializer.Initialize` + `VecExtension.TryLoad` pattern.

### Step 5: Update CI Filter Documentation

Add a comment in `.github/workflows/ci.yml` noting the available categories:

```yaml
# Available test categories: Unit, Database, Integration, LLM
# Run specific category: dotnet test --filter "Category=Unit"
```

## Files Modified

| File | Change Type |
|------|-------------|
| `tests/Qmd.Core.Tests/TestHelpers/TestDbHelper.cs` | **NEW** |
| All ~53 files in `tests/Qmd.Core.Tests/` | Add `[Trait]` attribute |
| All ~5 files in `tests/Qmd.Cli.Tests/` | Add `[Trait]` attribute |
| ~20 test classes with DB setup | Migrate to `TestDbHelper` |

## Test Strategy

Since this phase only modifies test code, verification is straightforward:

1. `dotnet test Qmd.slnx -c Release --filter "Category!=LLM"` -- all existing tests pass
2. `dotnet test Qmd.slnx -c Release --filter "Category=Unit"` -- runs only pure unit tests
3. `dotnet test Qmd.slnx -c Release --filter "Category=Database"` -- runs only database tests

## Risk Assessment

**LOW**. Only test code changes. No production code is touched. The only risk is accidentally breaking a test by changing its setup, which would be caught immediately by running the test suite.

## Dependencies

None. This phase is fully independent and can be executed before, after, or in parallel with Phases 1 and 3.
