namespace Qmd.Core.Llm;

/// <summary>
/// Manages LLM model lifecycle with inactivity-based disposal.
/// Models are loaded on first use and disposed after a configurable timeout of inactivity.
/// Ports the touchActivity/inactivity timer pattern from the TypeScript LlamaCpp class.
/// </summary>
internal class SessionManager : IAsyncDisposable
{
    private readonly LlamaSharpService service;
    private readonly int inactivityTimeoutMs;
    private readonly bool disposeModelsOnInactivity;
    private Timer? inactivityTimer;
    private readonly object @lock = new();
    private bool disposed;

    public SessionManager(LlamaSharpService service, SessionManagerOptions? options = null)
    {
        this.service = service;
        this.inactivityTimeoutMs = options?.InactivityTimeoutMs ?? LlmConstants.DefaultInactivityTimeoutMs;
        this.disposeModelsOnInactivity = options?.DisposeModelsOnInactivity ?? false;
    }

    /// <summary>
    /// Record activity to reset the inactivity timer.
    /// Call this before/after any LLM operation.
    /// </summary>
    public void TouchActivity()
    {
        lock (this.@lock)
        {
            if (this.disposed) return;
            this.inactivityTimer?.Dispose();
            this.inactivityTimer = new Timer(this.OnInactivityTimeout, null, this.inactivityTimeoutMs, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Execute an operation within a managed session, automatically tracking activity.
    /// </summary>
    public async Task<T> WithSessionAsync<T>(Func<ILlmService, Task<T>> operation, CancellationToken ct = default)
    {
        this.TouchActivity();
        try
        {
            return await operation(this.service);
        }
        finally
        {
            this.TouchActivity();
        }
    }

    /// <summary>
    /// Execute a void operation within a managed session.
    /// </summary>
    public async Task WithSessionAsync(Func<ILlmService, Task> operation, CancellationToken ct = default)
    {
        this.TouchActivity();
        try
        {
            await operation(this.service);
        }
        finally
        {
            this.TouchActivity();
        }
    }

    private void OnInactivityTimeout(object? state)
    {
        if (!this.disposeModelsOnInactivity) return;

        lock (this.@lock)
        {
            if (this.disposed) return;
            // Dispose the service asynchronously — fire-and-forget on background timer
            _ = Task.Run(async () =>
            {
                try
                {
                    await this.service.DisposeAsync();
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
        lock (this.@lock)
        {
            if (this.disposed) return;
            this.disposed = true;
            this.inactivityTimer?.Dispose();
            this.inactivityTimer = null;
        }

        await this.service.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

public class SessionManagerOptions
{
    public int InactivityTimeoutMs { get; init; } = LlmConstants.DefaultInactivityTimeoutMs;
    public bool DisposeModelsOnInactivity { get; init; }
}
