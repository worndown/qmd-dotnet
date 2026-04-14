namespace Qmd.Core.Database;

/// <summary>
/// Abstraction over SQLite database operations.
/// </summary>
public interface IQmdDatabase : IDisposable
{
    void Exec(string sql);
    IStatement Prepare(string sql);
    void LoadExtension(string path);
}
