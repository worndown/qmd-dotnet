# Phase 3: Error Handling Standardization

## Goal

Replace silent `catch { }` blocks with typed exception handlers, introduce domain-specific exception types, and ensure `CancellationToken` / `OperationCanceledException` is never swallowed. This fixes real bugs where failures are silently dropped, and establishes consistent error handling contracts for the service interfaces introduced in Phase 5.

## Why This Matters

Several places in the codebase catch all exceptions silently, including `OperationCanceledException` (which should always propagate for proper cancellation behavior):

- `EmbeddingPipeline.cs` line 79: bare `catch` on batch embed -- swallows cancellation
- `EmbeddingPipeline.cs` line 90: bare `catch` on per-chunk embed -- swallows cancellation
- `EmbeddingPipeline.cs` line 127: bare `catch` on per-doc processing -- swallows cancellation
- `QueryExpander.cs` line 84: `catch { return null; }` on JSON parsing
- `CollectionReindexer.cs` line 68: bare `catch` on `File.ReadAllTextAsync`
- `MaintenanceOperations.cs` lines 41-43: swallowed exception on vector table check

Additionally, the codebase uses generic `InvalidOperationException` for unrelated error scenarios, making it hard for callers to handle specific failures.

## Detailed Changes

### Step 1: Create Domain Exception Types

Create `src/Qmd.Core/QmdException.cs`:

```csharp
namespace Qmd.Core;

/// <summary>
/// Base exception for QMD domain errors.
/// </summary>
public class QmdException : Exception
{
    public QmdException(string message) : base(message) { }
    public QmdException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when an LLM operation fails or is not configured.
/// </summary>
public class QmdModelException : QmdException
{
    public QmdModelException(string message) : base(message) { }
    public QmdModelException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown for malformed search queries.
/// </summary>
public class QmdQueryException : QmdException
{
    public QmdQueryException(string message) : base(message) { }
}
```

Keep this minimal. Don't over-create exception types -- only add what has a distinct handling path.

### Step 2: Fix Silent Catch Blocks

#### `src/Qmd.Core/Embedding/EmbeddingPipeline.cs`

**Line 79 -- batch embed failure:**
```csharp
// Before:
catch
{
    // Batch failed -- fallback to per-chunk embedding
    embeddings = new List<EmbeddingResult?>(new EmbeddingResult?[formattedTexts.Count]);
    ...
}

// After:
catch (OperationCanceledException) { throw; }
catch (Exception)
{
    // Batch failed -- fallback to per-chunk embedding
    embeddings = new List<EmbeddingResult?>(new EmbeddingResult?[formattedTexts.Count]);
    ...
}
```

**Line 90 -- per-chunk embed failure:**
```csharp
// Before:
catch { embeddings[j] = null; }

// After:
catch (OperationCanceledException) { throw; }
catch (Exception) { embeddings[j] = null; }
```

**Line 127 -- per-doc processing failure:**
```csharp
// Before:
catch
{
    totalErrors++;
}

// After:
catch (OperationCanceledException) { throw; }
catch (Exception)
{
    totalErrors++;
}
```

#### `src/Qmd.Core/Search/QueryExpander.cs`

**Line ~84 -- JSON parsing failure:**
```csharp
// Before:
catch { return null; }

// After:
catch (JsonException) { return null; }
```

This is already a reasonable pattern (return null on malformed LLM output), but the catch should be type-specific to avoid hiding unexpected errors.

#### `src/Qmd.Core/Indexing/CollectionReindexer.cs`

**Line 68 -- file read failure:**
```csharp
// Before:
catch
{
    processed++;
    options?.OnProgress?.Invoke(...);
    continue;
}

// After:
catch (IOException)
{
    processed++;
    options?.OnProgress?.Invoke(...);
    continue;
}
catch (UnauthorizedAccessException)
{
    processed++;
    options?.OnProgress?.Invoke(...);
    continue;
}
```

#### `src/Qmd.Core/Indexing/MaintenanceOperations.cs`

**Lines ~41-43 -- vector table check:**

Read the file to determine the exact pattern, then replace the bare catch with a typed `catch (Microsoft.Data.Sqlite.SqliteException)`.

### Step 3: Replace Generic InvalidOperationException

#### `src/Qmd.Core/Store/QmdStore.cs` (line 78)

```csharp
// Before:
private ILlmService GetLlmService() =>
    LlmService ?? throw new InvalidOperationException("LLM service not configured...");

// After:
private ILlmService GetLlmService() =>
    LlmService ?? throw new QmdModelException("LLM service not configured. Call EmbedAsync or configure LlamaSharpService.");
```

#### `src/Qmd.Core/Configuration/ConfigManager.cs`

Check for `InvalidOperationException` throws related to duplicate collections. Replace with `QmdException` if they represent domain errors (not programming bugs).

### Step 4: Update CLI Exception Handling

#### `src/Qmd.Cli/Program.cs`

The CLI currently catches `InvalidOperationException`. Update to also catch `QmdException`:

```csharp
// Before:
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

// After:
catch (QmdException ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}
```

## Files Modified

| File | Change Type |
|------|-------------|
| `src/Qmd.Core/QmdException.cs` | **NEW** -- domain exception types |
| `src/Qmd.Core/Embedding/EmbeddingPipeline.cs` | Fix 3 bare catch blocks |
| `src/Qmd.Core/Search/QueryExpander.cs` | Type-specific catch |
| `src/Qmd.Core/Indexing/CollectionReindexer.cs` | Type-specific catch |
| `src/Qmd.Core/Indexing/MaintenanceOperations.cs` | Type-specific catch |
| `src/Qmd.Core/Store/QmdStore.cs` | Use QmdModelException |
| `src/Qmd.Core/Configuration/ConfigManager.cs` | Review exception types |
| `src/Qmd.Cli/Program.cs` | Catch QmdException |

## Test Strategy

### New tests to add

Create `tests/Qmd.Core.Tests/ErrorHandlingTests.cs`:

```csharp
[Trait("Category", "Unit")]
public class ErrorHandlingTests
{
    [Fact]
    public void GetLlmService_ThrowsQmdModelException_WhenNotConfigured()
    {
        // Access QmdStore without LLM configured
        // Verify QmdModelException (not InvalidOperationException)
    }

    [Fact]
    public async Task EmbeddingPipeline_PropagatesCancellation()
    {
        // Create a CancellationTokenSource, cancel it
        // Verify OperationCanceledException propagates through the pipeline
    }
}
```

### Existing tests

Tests that assert `InvalidOperationException` for LLM-not-configured scenarios will need to change to `QmdModelException`. Search for `typeof(InvalidOperationException)` and `Should().Throw<InvalidOperationException>()` in test files.

## Risk Assessment

**LOW-MEDIUM**. The main risk is:
1. Changing exception types that callers catch -- mitigated by updating the CLI catch and keeping `QmdModelException` as a subclass of `QmdException` (not `InvalidOperationException`). Any external SDK consumers catching `InvalidOperationException` would break, but since the only consumer is the CLI (in the same repo), this is safe.
2. Making previously-silent failures visible -- this is intentional. If any tests relied on silent failures, they'll now see exceptions, which reveals test gaps.

## Dependencies

None. This phase is independent and can run in parallel with Phases 1 and 2.
