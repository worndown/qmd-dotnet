using FluentAssertions;
using Qmd.Core.Database;
using Qmd.Core.Indexing;
using Qmd.Core.Store;

namespace Qmd.Core.Tests.Indexing;

[Trait("Category", "Integration")]
public sealed class CollectionReindexerTests : IDisposable
{
    private readonly QmdStore store;
    private readonly string tempDir;

    public CollectionReindexerTests()
    {
        this.store = new QmdStore(new SqliteDatabase(":memory:"));
        this.tempDir = Path.Combine(Path.GetTempPath(), $"qmd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.tempDir);
    }

    public void Dispose()
    {
        this.store.Dispose();
        try { Directory.Delete(this.tempDir, true); } catch { }
    }

    private void CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(this.tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Fact]
    public async Task Reindex_IndexesNewFiles()
    {
        this.CreateFile("readme.md", "# Hello\nThis is a readme.");
        this.CreateFile("guide.md", "# Guide\nGetting started.");

        var result = await CollectionReindexer.ReindexCollectionAsync(this.store, this.tempDir, "**/*.md", "docs", ct: TestContext.Current.CancellationToken);

        result.Indexed.Should().Be(2);
        result.Updated.Should().Be(0);
        result.Unchanged.Should().Be(0);
    }

    [Fact]
    public async Task Reindex_DetectsUnchanged()
    {
        this.CreateFile("readme.md", "# Hello\nContent.");

        await CollectionReindexer.ReindexCollectionAsync(this.store, this.tempDir, "**/*.md", "docs", ct: TestContext.Current.CancellationToken);
        var result = await CollectionReindexer.ReindexCollectionAsync(this.store, this.tempDir, "**/*.md", "docs", ct: TestContext.Current.CancellationToken);

        result.Indexed.Should().Be(0);
        result.Unchanged.Should().Be(1);
    }

    [Fact]
    public async Task Reindex_DetectsChangedContent()
    {
        this.CreateFile("readme.md", "# Hello\nOriginal content.");
        await CollectionReindexer.ReindexCollectionAsync(this.store, this.tempDir, "**/*.md", "docs", ct: TestContext.Current.CancellationToken);

        // Modify file
        this.CreateFile("readme.md", "# Hello\nUpdated content.");
        var result = await CollectionReindexer.ReindexCollectionAsync(this.store, this.tempDir, "**/*.md", "docs", ct: TestContext.Current.CancellationToken);

        result.Updated.Should().Be(1);
        result.Unchanged.Should().Be(0);
    }

    [Fact]
    public async Task Reindex_DeactivatesDeletedFiles()
    {
        this.CreateFile("a.md", "Content A");
        this.CreateFile("b.md", "Content B");
        await CollectionReindexer.ReindexCollectionAsync(this.store, this.tempDir, "**/*.md", "docs", ct: TestContext.Current.CancellationToken);

        // Delete one file
        File.Delete(Path.Combine(this.tempDir, "b.md"));
        var result = await CollectionReindexer.ReindexCollectionAsync(this.store, this.tempDir, "**/*.md", "docs", ct: TestContext.Current.CancellationToken);

        result.Removed.Should().Be(1);
    }

    [Fact]
    public async Task Reindex_ReportsProgress()
    {
        this.CreateFile("a.md", "Content A");
        this.CreateFile("b.md", "Content B");
        var progressReports = new List<Qmd.Core.Models.ReindexProgress>();

        await CollectionReindexer.ReindexCollectionAsync(this.store, this.tempDir, "**/*.md", "docs",
            new ReindexOptions { Progress = new TestHelpers.SyncProgress<Qmd.Core.Models.ReindexProgress>(p => progressReports.Add(p)) },
            ct: TestContext.Current.CancellationToken);

        progressReports.Should().HaveCount(2);
        progressReports.Last().Current.Should().Be(2);
        progressReports.Last().Total.Should().Be(2);
    }

    [Fact]
    public async Task Reindex_SkipsHiddenFiles()
    {
        this.CreateFile("readme.md", "Visible");
        this.CreateFile(".hidden.md", "Hidden");

        var result = await CollectionReindexer.ReindexCollectionAsync(this.store, this.tempDir, "**/*.md", "docs", ct: TestContext.Current.CancellationToken);
        result.Indexed.Should().Be(1);
    }

    [Fact]
    public async Task Reindex_SkipsEmptyFiles()
    {
        this.CreateFile("readme.md", "Content");
        this.CreateFile("empty.md", "   ");

        var result = await CollectionReindexer.ReindexCollectionAsync(this.store, this.tempDir, "**/*.md", "docs", ct: TestContext.Current.CancellationToken);
        result.Indexed.Should().Be(1);
    }

    [Fact]
    public async Task Reindex_FtsSearchableAfterIndex()
    {
        this.CreateFile("api.md", "# API Reference\nThis documents REST API endpoints.");
        await CollectionReindexer.ReindexCollectionAsync(this.store, this.tempDir, "**/*.md", "docs", ct: TestContext.Current.CancellationToken);

        var results = this.store.SearchFTS("API endpoints");
        results.Should().NotBeEmpty();
    }
}
