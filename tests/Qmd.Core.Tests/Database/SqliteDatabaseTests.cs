using FluentAssertions;
using Qmd.Core.Database;

namespace Qmd.Core.Tests.Database;

[Trait("Category", "Database")]
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
        var row = _db.Prepare("SELECT name FROM sqlite_master WHERE type='table' AND name='test'")
            .Get<NameRow>();
        row.Should().NotBeNull();
        row!.Name.Should().Be("test");
    }

    [Fact]
    public void Run_InsertReturnsChangesAndLastInsertRowid()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)");
        var result = _db.Prepare("INSERT INTO test (name) VALUES ($1)").Run("hello");
        result.Changes.Should().Be(1);
        result.LastInsertRowid.Should().Be(1);
    }

    [Fact]
    public void Run_UpdateReturnsChangesCount()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)");
        _db.Prepare("INSERT INTO test (name) VALUES ($1)").Run("a");
        _db.Prepare("INSERT INTO test (name) VALUES ($1)").Run("b");
        _db.Prepare("INSERT INTO test (name) VALUES ($1)").Run("c");

        var result = _db.Prepare("UPDATE test SET name = $1").Run("x");
        result.Changes.Should().Be(3);
    }

    [Fact]
    public void Get_MapsColumnsToTypedClass()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT, active INTEGER)");
        _db.Prepare("INSERT INTO test (name, active) VALUES ($1, $2)").Run("hello", 1L);

        var row = _db.Prepare("SELECT id, name, active FROM test WHERE id = $1").Get<TestRow>(1L);
        row.Should().NotBeNull();
        row!.Id.Should().Be(1);
        row.Name.Should().Be("hello");
        row.Active.Should().Be(1);
    }

    [Fact]
    public void Get_ReturnsNull_WhenNoRows()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)");
        var row = _db.Prepare("SELECT name FROM test WHERE id = $1").Get<NameRow>(999L);
        row.Should().BeNull();
    }

    [Fact]
    public void All_ReturnsRowsInOrder()
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
    public void All_ReturnsEmptyList_WhenNoRows()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)");
        var rows = _db.Prepare("SELECT name FROM test").All<NameRow>();
        rows.Should().NotBeNull();
        rows.Should().BeEmpty();
    }

    [Fact]
    public void Run_BindsNullParameterAsDbNull()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)");
        _db.Prepare("INSERT INTO test (name) VALUES ($1)").Run((object?)null);

        var row = _db.Prepare("SELECT name FROM test WHERE id = 1").Get<NullableNameRow>();
        row.Should().NotBeNull();
        row!.Name.Should().BeNull();
    }

    [Fact]
    public void MapRow_MapsSnakeCaseColumnToPascalCaseProperty()
    {
        _db.Exec("CREATE TABLE test (created_at TEXT, display_name TEXT)");
        _db.Prepare("INSERT INTO test (created_at, display_name) VALUES ($1, $2)")
            .Run("2025-01-01", "Alice");

        var row = _db.Prepare("SELECT created_at, display_name FROM test").Get<SnakeCaseRow>();
        row.Should().NotBeNull();
        row!.CreatedAt.Should().Be("2025-01-01");
        row.DisplayName.Should().Be("Alice");
    }

    [Fact]
    public void MapRow_MatchesPropertyCaseInsensitively()
    {
        _db.Exec("CREATE TABLE test (name TEXT)");
        _db.Prepare("INSERT INTO test (name) VALUES ($1)").Run("alice");

        // Column "name" (lowercase) should bind to property "Name" (PascalCase).
        var row = _db.Prepare("SELECT name FROM test").Get<NameRow>();
        row!.Name.Should().Be("alice");
    }

    [Fact]
    public void MapRow_IgnoresColumnWithNoMatchingProperty()
    {
        _db.Exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT, extra TEXT)");
        _db.Prepare("INSERT INTO test (name, extra) VALUES ($1, $2)").Run("hi", "junk");

        var act = () => _db.Prepare("SELECT id, name, extra FROM test").Get<TestRow>();
        act.Should().NotThrow();
        act()!.Name.Should().Be("hi");
    }

    [Fact]
    public void MapRow_IgnoresReadOnlyProperty()
    {
        _db.Exec("CREATE TABLE test (name TEXT, computed TEXT)");
        _db.Prepare("INSERT INTO test (name, computed) VALUES ($1, $2)").Run("hi", "ignored");

        var row = _db.Prepare("SELECT name, computed FROM test").Get<ReadOnlyPropRow>();
        row!.Name.Should().Be("hi");
        row.Computed.Should().Be("default"); // unchanged
    }

    [Fact]
    public void MapRow_CoercesLongToInt()
    {
        _db.Exec("CREATE TABLE test (n INTEGER)");
        _db.Prepare("INSERT INTO test (n) VALUES ($1)").Run(42L);

        var row = _db.Prepare("SELECT n FROM test").Get<IntRow>();
        row!.N.Should().Be(42);
    }

    [Theory]
    [InlineData(0L, false)]
    [InlineData(1L, true)]
    public void MapRow_CoercesLongToBool(long stored, bool expected)
    {
        _db.Exec("CREATE TABLE test (flag INTEGER)");
        _db.Prepare("INSERT INTO test (flag) VALUES ($1)").Run(stored);

        var row = _db.Prepare("SELECT flag FROM test").Get<BoolRow>();
        row!.Flag.Should().Be(expected);
    }

    [Fact]
    public void MapRow_CoercesValueToStringViaToString()
    {
        _db.Exec("CREATE TABLE test (n INTEGER)");
        _db.Prepare("INSERT INTO test (n) VALUES ($1)").Run(7L);

        var row = _db.Prepare("SELECT n FROM test").Get<StringFromIntRow>();
        row!.N.Should().Be("7");
    }

    [Fact]
    public void MapRow_CoercesLongToNullableInt()
    {
        _db.Exec("CREATE TABLE test (n INTEGER)");
        _db.Prepare("INSERT INTO test (n) VALUES ($1)").Run(99L);

        var row = _db.Prepare("SELECT n FROM test").Get<NullableIntRow>();
        row!.N.Should().Be(99);
    }

    [Fact]
    public void MapRow_MapsDbNullToNull_ForNullableValueType()
    {
        _db.Exec("CREATE TABLE test (n INTEGER)");
        _db.Prepare("INSERT INTO test (n) VALUES ($1)").Run((object?)null);

        var row = _db.Prepare("SELECT n FROM test").Get<NullableIntRow>();
        row!.N.Should().BeNull();
    }

    [Fact]
    public void MapRow_MapsDbNullToNull_ForReferenceType()
    {
        _db.Exec("CREATE TABLE test (name TEXT)");
        _db.Prepare("INSERT INTO test (name) VALUES ($1)").Run((object?)null);

        var row = _db.Prepare("SELECT name FROM test").Get<NullableNameRow>();
        row!.Name.Should().BeNull();
    }

    private class NameRow
    {
        public string Name { get; set; } = "";
    }

    private class NullableNameRow
    {
        public string? Name { get; set; }
    }

    private class TestRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int Active { get; set; }
    }

    private class SnakeCaseRow
    {
        public string CreatedAt { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    private class ReadOnlyPropRow
    {
        public string Name { get; set; } = "";
        public string Computed { get; } = "default";
    }

    private class IntRow
    {
        public int N { get; set; }
    }

    private class BoolRow
    {
        public bool Flag { get; set; }
    }

    private class StringFromIntRow
    {
        public string N { get; set; } = "";
    }

    private class NullableIntRow
    {
        public int? N { get; set; }
    }
}
