using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Qmd.Core.Database;

/// <summary>
/// SQLite implementation of IStatement using Microsoft.Data.Sqlite.
/// Wraps a SqliteCommand for the prepare/run/get/all pattern.
/// </summary>
internal class SqliteStatement : IStatement
{
    private readonly SqliteConnection connection;
    private readonly string sql;

    public SqliteStatement(SqliteConnection connection, string sql)
    {
        this.connection = connection;
        this.sql = sql;
    }

    public StatementResult Run(params object?[] parameters)
    {
        using var cmd = this.CreateCommand(parameters);
        var changes = cmd.ExecuteNonQuery();

        // Get last insert rowid
        using var rowidCmd = this.connection.CreateCommand();
        rowidCmd.CommandText = "SELECT last_insert_rowid()";
        var rowid = (long)(rowidCmd.ExecuteScalar() ?? 0L);

        return new StatementResult(changes, rowid);
    }

    public T? Get<T>(params object?[] parameters) where T : class, new()
    {
        using var cmd = this.CreateCommand(parameters);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return MapRow<T>(reader);
    }

    public List<T> All<T>(params object?[] parameters) where T : class, new()
    {
        using var cmd = this.CreateCommand(parameters);
        using var reader = cmd.ExecuteReader();
        var results = new List<T>();
        while (reader.Read())
        {
            results.Add(MapRow<T>(reader));
        }
        return results;
    }

    private SqliteCommand CreateCommand(object?[] parameters)
    {
        var cmd = this.connection.CreateCommand();
        cmd.CommandText = this.sql;
        for (int i = 0; i < parameters.Length; i++)
        {
            cmd.Parameters.AddWithValue($"${i + 1}", parameters[i] ?? DBNull.Value);
        }
        return cmd;
    }

    private static T MapRow<T>(SqliteDataReader reader) where T : class, new()
    {
        var obj = new T();
        var type = typeof(T);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);
            var value = reader.IsDBNull(i) ? null : reader.GetValue(i);

            // Try property match (case-insensitive, also try PascalCase)
            var prop = type.GetProperty(columnName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?? type.GetProperty(ToPascalCase(columnName), BindingFlags.Public | BindingFlags.Instance);

            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(obj, ConvertValue(value, prop.PropertyType));
            }
        }
        return obj;
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null) return null;

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying == typeof(int) && value is long longVal)
            return (int)longVal;
        if (underlying == typeof(bool) && value is long boolLong)
            return boolLong != 0;
        if (underlying == typeof(string) && value is not string)
            return value.ToString();

        return value;
    }

    private static string ToPascalCase(string snakeCase)
    {
        return string.Concat(snakeCase.Split('_')
            .Select(s => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s));
    }

    public void Dispose()
    {
        // SqliteStatement is lightweight — nothing to dispose.
        // The SqliteCommand is created and disposed per-call.
    }
}
