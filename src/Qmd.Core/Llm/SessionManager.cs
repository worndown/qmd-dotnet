namespace Qmd.Core.Llm;

/// <summary>
/// Manages LLM model lifecycle with inactivity-based disposal.
/// Models are loaded on first use and disposed after a configurable timeout of inactivity.
/// Ports the touchActivity/inactivity timer pattern from the TypeScript LlamaCpp class.
/// </summary>
internal class SessionManager : IAsyncDisposable
{
    private readonly LlamaSharpService _service;
    private readonly int _inactivityTimeoutMs;
    private readonly bool _disposeModelsOnInactivity;
    private Timer? _inactivityTimer;
    private readonly object _lock = new();
    private bool _disposed;

    public SessionManager(LlamaSharpService service, SessionManagerOptions? options = null)
    {
        _service = service;
        _inactivityTimeoutMs = options?.InactivityTimeoutMs ?? LlmConstants.DefaultInactivityTimeoutMs;
        _disposeModelsOnInactivity = options?.DisposeModelsOnInactivity ?? false;
    }

    /// <summary>
    /// Record activity to reset the inactivity timer.
    /// Call this before/after any LLM operation.
    /// </summary>
    public void TouchActivity()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _inactivityTimer?.Dispose();
            _inactivityTimer = new Timer(OnInactivityTimeout, null, _inactivityTimeoutMs, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Execute an operation within a managed session, automatically tracking activity.
    /// </summary>
    public async Task<T> WithSessionAsync<T>(Func<ILlmService, Task<T>> operation, CancellationToken ct = default)
    {
        TouchActivity();
        try
        {
            return await operation(_service);
        }
        finally
        {
            TouchActivity();
        }
    }

    /// <summary>
    /// Execute a void operation within a managed session.
    /// </summary>
    public async Task WithSessionAsync(Func<ILlmService, Task> operation, CancellationToken ct = default)
    {
        TouchActivity();
        try
        {
            await operation(_service);
        }
        finally
        {
            TouchActivity();
        }
    }

    private void OnInactivityTimeout(object? state)
    {
        if (!_disposeModelsOnInactivity) return;

        lock (_lock)
        {
            if (_disposed) return;
            // Dispose the service asynchronously — fire-and-forget on background timer
            _ = Task.Run(async () =>
            {
                try
                {
                    await _service.DisposeAsync();
                }
                catch
                {
                    // Background cleanup — nothing to propagate to
                }
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _inactivityTimer?.Dispose();
            _inactivityTimer = null;
        }

        await _service.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

public class SessionManagerOptions
{
    public int InactivityTimeoutMs { get; init; } = LlmConstants.DefaultInactivityTimeoutMs;
    public bool DisposeModelsOnInactivity { get; init; }
}
