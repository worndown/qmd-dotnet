using Microsoft.Data.Sqlite;

namespace Qmd.Core.Database;

/// <summary>
/// SQLite implementation of IQmdDatabase using Microsoft.Data.Sqlite.
/// Maintains a single open connection for the lifetime of the store.
/// </summary>
internal class SqliteDatabase : IQmdDatabase
{
    private readonly SqliteConnection connection;

    public SqliteDatabase(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = path == ":memory:" ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        this.connection = new SqliteConnection(connectionString);
        this.connection.Open();
    }

    public void Exec(string sql)
    {
        using var cmd = this.connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public IStatement Prepare(string sql)
    {
        return new SqliteStatement(this.connection, sql);
    }

    public void LoadExtension(string path)
    {
        this.connection.LoadExtension(path);
    }

    public void Dispose()
    {
        this.connection.Dispose();
    }
}
