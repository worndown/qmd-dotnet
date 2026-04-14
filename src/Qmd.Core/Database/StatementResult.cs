namespace Qmd.Core.Database;

/// <summary>
/// Result from executing a SQL statement.
/// </summary>
public readonly record struct StatementResult(int Changes, long LastInsertRowid);
