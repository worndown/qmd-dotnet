# Phase 7: CLI Console I/O Abstraction

## Goal

Abstract `Console.Write` / `Console.Error.WriteLine` behind an `IConsoleOutput` interface in the CLI project, enabling testable CLI command output without process spawning.

## Why This Matters

All CLI commands write directly to `Console.Write()` and `Console.Error.WriteLine()`. This means:
- CLI output formatting cannot be unit-tested without spawning a child process
- The `CliIntegrationTests` test against the SDK layer, not the actual CLI output
- Progress reporting uses `Console.Error` directly (`TerminalProgress`)

An `IConsoleOutput` interface allows tests to capture and assert on CLI output.

## Detailed Changes

### Step 1: Create IConsoleOutput Interface

Create `src/Qmd.Cli/IConsoleOutput.cs`:

```csharp
namespace Qmd.Cli;

/// <summary>
/// Abstraction over console I/O for testability.
/// </summary>
internal interface IConsoleOutput
{
    void Write(string text);
    void WriteLine(string text);
    void WriteError(string text);
    void WriteErrorLine(string text);
    bool IsOutputRedirected { get; }
    bool IsErrorRedirected { get; }
}
```

Create `src/Qmd.Cli/SystemConsoleOutput.cs`:

```csharp
namespace Qmd.Cli;

internal class SystemConsoleOutput : IConsoleOutput
{
    public void Write(string text) => Console.Write(text);
    public void WriteLine(string text) => Console.WriteLine(text);
    public void WriteError(string text) => Console.Error.Write(text);
    public void WriteErrorLine(string text) => Console.Error.WriteLine(text);
    public bool IsOutputRedirected => Console.IsOutputRedirected;
    public bool IsErrorRedirected => Console.IsErrorRedirected;
}
```

### Step 2: Update Command Classes

Each command's `Create()` method returns a `Command` with an action handler. The handler currently captures `Console` directly. Update to accept `IConsoleOutput`:

**Option A (recommended)**: Use a static/shared `IConsoleOutput` instance set during startup:

```csharp
// In Program.cs or a shared location:
internal static class CliContext
{
    public static IConsoleOutput Console { get; set; } = new SystemConsoleOutput();
}
```

Then in commands:
```csharp
// Before:
Console.Write(output);

// After:
CliContext.Console.Write(output);
```

**Option B**: Pass `IConsoleOutput` through `System.CommandLine`'s `InvocationContext`. This is cleaner but more involved with System.CommandLine 2.x.

For simplicity, Option A is recommended. It's a single search-and-replace for `Console.Write` -> `CliContext.Console.Write`.

### Step 3: Update Formatters

Update the formatter classes that produce output:

- `src/Qmd.Cli/Formatting/SearchResultFormatter.cs`
- `src/Qmd.Cli/Formatting/DocumentFormatter.cs`
- `src/Qmd.Cli/Formatting/SingleDocumentFormatter.cs`
- `src/Qmd.Cli/Formatting/FormatHelpers.cs`

These formatters should either:
- Return strings (preferred -- let the caller write to console)
- Accept `IConsoleOutput` parameter

Check the current pattern: if formatters already return strings, no change is needed. If they write directly to Console, refactor to return strings.

### Step 4: Update Progress Reporting

`src/Qmd.Cli/Progress/TerminalProgress.cs` uses `Console.Error` for progress bars. Update to use `IConsoleOutput`:

```csharp
// Before:
Console.Error.Write($"\r{progressBar}");

// After:
CliContext.Console.WriteError($"\r{progressBar}");
```

### Step 5: Update Error Output in Program.cs

```csharp
// Before:
Console.Error.WriteLine($"ERROR: {ex.Message}");

// After:
CliContext.Console.WriteErrorLine($"ERROR: {ex.Message}");
```

### Step 6: Create Test Console

Create `tests/Qmd.Cli.Tests/TestConsoleOutput.cs`:

```csharp
namespace Qmd.Cli.Tests;

internal class TestConsoleOutput : IConsoleOutput
{
    private readonly StringBuilder _output = new();
    private readonly StringBuilder _error = new();

    public void Write(string text) => _output.Append(text);
    public void WriteLine(string text) => _output.AppendLine(text);
    public void WriteError(string text) => _error.Append(text);
    public void WriteErrorLine(string text) => _error.AppendLine(text);
    public bool IsOutputRedirected => true;
    public bool IsErrorRedirected => true;

    public string GetOutput() => _output.ToString();
    public string GetError() => _error.ToString();
    public void Clear() { _output.Clear(); _error.Clear(); }
}
```

### Step 7: Add CLI Output Tests

With `TestConsoleOutput`, add tests that verify actual CLI command output formatting:

```csharp
[Fact]
public async Task SearchCommand_OutputsJsonFormat()
{
    var console = new TestConsoleOutput();
    CliContext.Console = console;

    // Execute search command against seeded store
    // ...

    var output = console.GetOutput();
    var json = JsonDocument.Parse(output);
    json.RootElement.GetArrayLength().Should().BeGreaterThan(0);
}
```

## Files Modified

| File | Change Type |
|------|-------------|
| `src/Qmd.Cli/IConsoleOutput.cs` | **NEW** |
| `src/Qmd.Cli/SystemConsoleOutput.cs` | **NEW** |
| `src/Qmd.Cli/CliContext.cs` | **NEW** (shared context) |
| `src/Qmd.Cli/Program.cs` | Set `CliContext.Console`, update error output |
| `src/Qmd.Cli/Commands/*.cs` | Replace `Console.Write` with `CliContext.Console.Write` |
| `src/Qmd.Cli/Formatting/*.cs` | Update if they write to Console directly |
| `src/Qmd.Cli/Progress/TerminalProgress.cs` | Use `CliContext.Console` |
| `tests/Qmd.Cli.Tests/TestConsoleOutput.cs` | **NEW** |

## Test Strategy

- All existing tests pass (no behavioral change)
- New tests use `TestConsoleOutput` to verify formatted output

## Risk Assessment

**LOW**. CLI-only change. Does not affect `Qmd.Core` at all. The `CliContext.Console` pattern is simple and the replacement is mechanical.

## Dependencies

Independent of other phases, but recommended after Phase 5 since the overall architecture is cleaner. Can also be done earlier if CLI testability is a priority.

## Verification

1. `dotnet build Qmd.slnx -c Release` -- must compile
2. `dotnet test Qmd.slnx -c Release --filter "Category!=LLM"` -- all tests pass
3. Manual test: run several `qmd` commands and verify output is unchanged
