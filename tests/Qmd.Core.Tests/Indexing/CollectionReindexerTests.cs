using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Indexing;
using Qmd.Core.Store;

namespace Qmd.Core.Tests.Indexing;

[Trait("Category", "Integration")]
public class CollectionReindexerTests : IDisposable
{
    private readonly QmdStore _store;
    private readonly string _tempDir;

    public CollectionReindexerTests()
    {
        _store = new QmdStore(new SqliteDatabase(":memory:"));
        _tempDir = Path.Combine(Path.GetTempPath(), $"qmd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private void CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Fact]
    public async Task Reindex_IndexesNewFiles()
    {
        CreateFile("readme.md", "# Hello\nThis is a readme.");
        CreateFile("guide.md", "# Guide\nGetting started.");

        var result = await CollectionReindexer.ReindexCollectionAsync(
            _store, _tempDir, "**/*.md", "docs");

        result.Indexed.Should().Be(2);
        result.Updated.Should().Be(0);
        result.Unchanged.Should().Be(0);
    }

    [Fact]
    public async Task Reindex_DetectsUnchanged()
    {
        CreateFile("readme.md", "# Hello\nContent.");

        await CollectionReindexer.ReindexCollectionAsync(_store, _tempDir, "**/*.md", "docs");
        var result = await CollectionReindexer.ReindexCollectionAsync(_store, _tempDir, "**/*.md", "docs");

        result.Indexed.Should().Be(0);
        result.Unchanged.Should().Be(1);
    }

    [Fact]
    public async Task Reindex_DetectsChangedContent()
    {
        CreateFile("readme.md", "# Hello\nOriginal content.");
        await CollectionReindexer.ReindexCollectionAsync(_store, _tempDir, "**/*.md", "docs");

        // Modify file
        CreateFile("readme.md", "# Hello\nUpdated content.");
        var result = await CollectionReindexer.ReindexCollectionAsync(_store, _tempDir, "**/*.md", "docs");

        result.Updated.Should().Be(1);
        result.Unchanged.Should().Be(0);
    }

    [Fact]
    public async Task Reindex_DeactivatesDeletedFiles()
    {
        CreateFile("a.md", "Content A");
        CreateFile("b.md", "Content B");
        await CollectionReindexer.ReindexCollectionAsync(_store, _tempDir, "**/*.md", "docs");

        // Delete one file
        File.Delete(Path.Combine(_tempDir, "b.md"));
        var result = await CollectionReindexer.ReindexCollectionAsync(_store, _tempDir, "**/*.md", "docs");

        result.Removed.Should().Be(1);
    }

    [Fact]
    public async Task Reindex_ReportsProgress()
    {
        CreateFile("a.md", "Content A");
        CreateFile("b.md", "Content B");
        var progressReports = new List<Qmd.Core.Models.ReindexProgress>();

        await CollectionReindexer.ReindexCollectionAsync(_store, _tempDir, "**/*.md", "docs",
            new ReindexOptions { OnProgress = p => progressReports.Add(p) });

        progressReports.Should().HaveCount(2);
        progressReports.Last().Current.Should().Be(2);
        progressReports.Last().Total.Should().Be(2);
    }

    [Fact]
    public async Task Reindex_SkipsHiddenFiles()
    {
        CreateFile("readme.md", "Visible");
        CreateFile(".hidden.md", "Hidden");

        var result = await CollectionReindexer.ReindexCollectionAsync(_store, _tempDir, "**/*.md", "docs");
        result.Indexed.Should().Be(1);
    }

    [Fact]
    public async Task Reindex_SkipsEmptyFiles()
    {
        CreateFile("readme.md", "Content");
        CreateFile("empty.md", "   ");

        var result = await CollectionReindexer.ReindexCollectionAsync(_store, _tempDir, "**/*.md", "docs");
        result.Indexed.Should().Be(1);
    }

    [Fact]
    public async Task Reindex_FtsSearchableAfterIndex()
    {
        CreateFile("api.md", "# API Reference\nThis documents REST API endpoints.");
        await CollectionReindexer.ReindexCollectionAsync(_store, _tempDir, "**/*.md", "docs");

        var results = _store.SearchFTS("API endpoints");
        results.Should().NotBeEmpty();
    }
}
