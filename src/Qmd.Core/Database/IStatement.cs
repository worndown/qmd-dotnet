namespace Qmd.Core.Database;

/// <summary>
/// Abstraction over a prepared SQL statement.
/// Mirrors the TypeScript Statement interface from db.ts.
/// </summary>
public interface IStatement : IDisposable
{
    StatementResult Run(params object?[] parameters);
    T? Get<T>(params object?[] parameters) where T : class, new();
    List<T> All<T>(params object?[] parameters) where T : class, new();

    /// <summary>Get a single row as a dynamic dictionary.</summary>
    Dictionary<string, object?>? GetDynamic(params object?[] parameters);

    /// <summary>Get all rows as dynamic dictionaries.</summary>
    List<Dictionary<string, object?>> AllDynamic(params object?[] parameters);
}
