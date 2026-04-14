# Phase 6: Replace Action Callbacks with IProgress<T>

## Goal

Replace `Action<T>` progress callbacks with `IProgress<T>`, the standard .NET pattern for reporting progress from async operations. This aligns the API with framework conventions and enables proper synchronization context handling.

## Why This Matters

The codebase currently uses `Action<T>` for progress reporting:

```csharp
// Current pattern (TypeScript-style callback):
public class UpdateOptions
{
    public Action<ReindexProgress>? OnProgress { get; init; }
}

// .NET standard pattern:
public class UpdateOptions
{
    public IProgress<ReindexProgress>? Progress { get; init; }
}
```

`IProgress<T>` is the .NET standard for several reasons:
- `Progress<T>` (the standard implementation) marshals callbacks to the captured `SynchronizationContext`, which is critical for UI applications
- It's the expected pattern -- any .NET developer reading `Action<ReindexProgress>` will wonder why `IProgress<T>` wasn't used
- It composes better with `System.Threading.Channels`, `IAsyncEnumerable`, and other async patterns

## Detailed Changes

### Step 1: Update Option Types

#### `src/Qmd.Core/IQmdStore.cs` -- UpdateOptions

```csharp
// Before:
public class UpdateOptions
{
    public List<string>? Collections { get; init; }
    public Action<ReindexProgress>? OnProgress { get; init; }
}

// After:
public class UpdateOptions
{
    public List<string>? Collections { get; init; }
    public IProgress<ReindexProgress>? Progress { get; init; }
}
```

#### `src/Qmd.Core/Models/EmbeddingTypes.cs` -- EmbedPipelineOptions

```csharp
// Before:
public class EmbedPipelineOptions
{
    // ... other props ...
    public Action<EmbedProgress>? OnProgress { get; init; }
}

// After:
public class EmbedPipelineOptions
{
    // ... other props ...
    public IProgress<EmbedProgress>? Progress { get; init; }
}
```

#### `src/Qmd.Core/Indexing/ReindexOptions.cs` (or CollectionReindexer.cs)

```csharp
// Before:
internal class ReindexOptions
{
    public List<string>? IgnorePatterns { get; init; }
    public Action<ReindexProgress>? OnProgress { get; init; }
}

// After:
internal class ReindexOptions
{
    public List<string>? IgnorePatterns { get; init; }
    public IProgress<ReindexProgress>? Progress { get; init; }
}
```

### Step 2: Update Service Implementations

#### `src/Qmd.Core/Indexing/CollectionReindexer.cs`

```csharp
// Before:
options?.OnProgress?.Invoke(new ReindexProgress(relativeFile, processed, total));

// After:
options?.Progress?.Report(new ReindexProgress(relativeFile, processed, total));
```

#### `src/Qmd.Core/Embedding/EmbeddingPipeline.cs`

```csharp
// Before:
options.OnProgress?.Invoke(new EmbedProgress(...));

// After:
options.Progress?.Report(new EmbedProgress(...));
```

#### `src/Qmd.Core/Store/QmdStore.cs` -- UpdateAsync

```csharp
// Before:
var result = await CollectionReindexer.ReindexCollectionAsync(
    this, coll.Path, coll.Pattern, coll.Name,
    new ReindexOptions
    {
        IgnorePatterns = coll.Ignore,
        OnProgress = options?.OnProgress,
    });

// After:
var result = await _reindexer.ReindexCollectionAsync(
    coll.Path, coll.Pattern, coll.Name,
    new ReindexOptions
    {
        IgnorePatterns = coll.Ignore,
        Progress = options?.Progress,
    });
```

### Step 3: Update LlmServiceFactory / ModelResolver Progress

#### `src/Qmd.Core/Llm/LlmServiceFactory.cs`

```csharp
// Before:
public static Task<string> ResolveModelAsync(string modelUri, bool autoDownload,
    Action<string>? onProgress = null, CancellationToken ct = default)

// After:
public static Task<string> ResolveModelAsync(string modelUri, bool autoDownload,
    IProgress<string>? progress = null, CancellationToken ct = default)
```

#### `src/Qmd.Core/Llm/ModelResolver.cs`

Same pattern change for any progress callbacks.

### Step 4: Update CLI Callers

#### `src/Qmd.Cli/Commands/UpdateCommand.cs`

```csharp
// Before:
var result = await store.UpdateAsync(new UpdateOptions
{
    Collections = collections,
    OnProgress = progress => { /* update terminal */ },
});

// After:
var result = await store.UpdateAsync(new UpdateOptions
{
    Collections = collections,
    Progress = new Progress<ReindexProgress>(progress => { /* update terminal */ }),
});
```

#### `src/Qmd.Cli/Commands/EmbedCommand.cs`

Same pattern -- wrap the lambda in `new Progress<EmbedProgress>(...)`.

#### Other CLI commands that pass progress callbacks

Search for `OnProgress` in the CLI project and update each site.

### Step 5: Update the `ensureVecTable` Callback

The `EmbeddingPipeline.GenerateEmbeddingsAsync` has an `Action<int>? ensureVecTable` parameter. This is not a progress callback -- it's a side-effecting hook. After Phase 5 (DI), this should become a method call on `IVecExtension` or be handled by the `IEmbeddingRepository`. If Phase 5 has already converted this, no change is needed here. If not, leave this callback as-is (it's not a progress pattern).

## Files Modified

| File | Change Type |
|------|-------------|
| `src/Qmd.Core/IQmdStore.cs` | `UpdateOptions.OnProgress` -> `Progress` |
| `src/Qmd.Core/Models/EmbeddingTypes.cs` | `EmbedPipelineOptions.OnProgress` -> `Progress` |
| `src/Qmd.Core/Indexing/CollectionReindexer.cs` (or ReindexOptions) | `OnProgress` -> `Progress` |
| `src/Qmd.Core/Embedding/EmbeddingPipeline.cs` | `.Invoke()` -> `.Report()` |
| `src/Qmd.Core/Store/QmdStore.cs` | Pass through `Progress` |
| `src/Qmd.Core/Llm/LlmServiceFactory.cs` | `Action<string>?` -> `IProgress<string>?` |
| `src/Qmd.Core/Llm/ModelResolver.cs` | Same |
| `src/Qmd.Cli/Commands/UpdateCommand.cs` | Wrap in `Progress<T>` |
| `src/Qmd.Cli/Commands/EmbedCommand.cs` | Wrap in `Progress<T>` |

## Test Strategy

Update tests that pass progress callbacks:

```csharp
// Before:
var receivedProgress = new List<ReindexProgress>();
await store.UpdateAsync(new UpdateOptions { OnProgress = p => receivedProgress.Add(p) });

// After:
var receivedProgress = new List<ReindexProgress>();
await store.UpdateAsync(new UpdateOptions { Progress = new Progress<ReindexProgress>(p => receivedProgress.Add(p)) });
```

**Note**: `Progress<T>` posts to `SynchronizationContext.Current`, which is `null` in test runners (no UI context). This means the callback runs on a thread pool thread asynchronously. For test assertions that check progress counts, you may need to use a custom `IProgress<T>` implementation that invokes synchronously:

```csharp
class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    public SyncProgress(Action<T> handler) => _handler = handler;
    public void Report(T value) => _handler(value);
}
```

Use `SyncProgress<T>` in tests instead of `Progress<T>` to avoid race conditions.

## Risk Assessment

**LOW**. `IProgress<T>.Report()` is a drop-in replacement for `Action<T>.Invoke()`. The only subtlety is the `Progress<T>` synchronization behavior in tests, which is solved by the `SyncProgress<T>` helper above.

## Dependencies

Should come after Phase 5 (DI refactoring) since Phase 5 changes the method signatures anyway. Doing this during Phase 5 would be ideal but separating it keeps each phase focused.

## Verification

1. `dotnet build Qmd.slnx -c Release` -- must compile
2. `dotnet test Qmd.slnx -c Release --filter "Category!=LLM"` -- all tests pass
3. Manual test: run `qmd update` and `qmd embed` from CLI, verify progress output still works
