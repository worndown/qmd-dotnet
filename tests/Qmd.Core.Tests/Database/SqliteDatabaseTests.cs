using FluentAssertions;
using Qmd.Core.Database;

namespace Qmd.Core.Tests.Database;

public class SqliteDatabaseTests : IDisposable
{
    private readonly SqliteDatabase _db;

    public SqliteDatabaseTests()
    {
        _db = new SqliteDatabase(":memory:");
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Exec_CreatesTable()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)");
        var result = _db.Prepare("SELECT name FROM sqlite_master WHERE type='table' AND name='test'")
            .GetDynamic();
        result.Should().NotBeNull();
        result!["name"].Should().Be("test");
    }

    [Fact]
    public void Prepare_Run_InsertsRow()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)");
        var result = _db.Prepare("INSERT INTO test (name) VALUES ($1)").Run("hello");
        result.Changes.Should().Be(1);
        result.LastInsertRowid.Should().Be(1);
    }

    [Fact]
    public void Prepare_Get_ReturnsSingleRow()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT, value INTEGER)");
        _db.Prepare("INSERT INTO test (name, value) VALUES ($1, $2)").Run("hello", 42);

        var row = _db.Prepare("SELECT name, value FROM test WHERE id = $1").GetDynamic(1L);
        row.Should().NotBeNull();
        row!["name"].Should().Be("hello");
        row["value"].Should().Be(42L);
    }

    [Fact]
    public void Prepare_Get_ReturnsNull_WhenNoRows()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY)");
        var row = _db.Prepare("SELECT * FROM test WHERE id = $1").GetDynamic(999L);
        row.Should().BeNull();
    }

    [Fact]
    public void Prepare_All_ReturnsMultipleRows()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)");
        _db.Prepare("INSERT INTO test (name) VALUES ($1)").Run("alice");
        _db.Prepare("INSERT INTO test (name) VALUES ($1)").Run("bob");
        _db.Prepare("INSERT INTO test (name) VALUES ($1)").Run("charlie");

        var rows = _db.Prepare("SELECT name FROM test ORDER BY name").AllDynamic();
        rows.Should().HaveCount(3);
        rows[0]["name"].Should().Be("alice");
        rows[1]["name"].Should().Be("bob");
        rows[2]["name"].Should().Be("charlie");
    }

    [Fact]
    public void Prepare_GenericGet_MapsToClass()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT, active INTEGER)");
        _db.Prepare("INSERT INTO test (name, active) VALUES ($1, $2)").Run("hello", 1L);

        var row = _db.Prepare("SELECT id, name, active FROM test WHERE id = $1").Get<TestRow>(1L);
        row.Should().NotBeNull();
        row!.Name.Should().Be("hello");
        row.Active.Should().Be(1);
    }

    [Fact]
    public void Prepare_GenericAll_MapsToClass()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT, active INTEGER)");
        _db.Prepare("INSERT INTO test (name, active) VALUES ($1, $2)").Run("a", 1L);
        _db.Prepare("INSERT INTO test (name, active) VALUES ($1, $2)").Run("b", 0L);

        var rows = _db.Prepare("SELECT id, name, active FROM test ORDER BY name").All<TestRow>();
        rows.Should().HaveCount(2);
        rows[0].Name.Should().Be("a");
        rows[1].Name.Should().Be("b");
    }

    [Fact]
    public void Prepare_HandlesNullParameters()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)");
        _db.Prepare("INSERT INTO test (name) VALUES ($1)").Run((object?)null);

        var row = _db.Prepare("SELECT name FROM test WHERE id = 1").GetDynamic();
        row.Should().NotBeNull();
        row!["name"].Should().BeNull();
    }

    private class TestRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int Active { get; set; }
    }
}
