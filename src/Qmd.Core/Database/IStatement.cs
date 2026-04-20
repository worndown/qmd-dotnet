namespace Qmd.Core.Database;

/// <summary>
/// Abstraction over a prepared SQL statement.
/// </summary>
public interface IStatement : IDisposable
{
    StatementResult Run(params object?[] parameters);
    T? Get<T>(params object?[] parameters) where T : class, new();
    List<T> All<T>(params object?[] parameters) where T : class, new();
}
