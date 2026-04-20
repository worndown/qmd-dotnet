namespace Qmd.Core.Tests.TestHelpers;

/// <summary>
/// Synchronous IProgress&lt;T&gt; implementation for tests.
/// Unlike <see cref="Progress{T}"/>, this invokes the handler inline
/// (no SynchronizationContext post), avoiding race conditions in assertions.
/// </summary>
internal class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> handler;
    public SyncProgress(Action<T> handler) => this.handler = handler;
    public void Report(T value) => this.handler(value);
}
