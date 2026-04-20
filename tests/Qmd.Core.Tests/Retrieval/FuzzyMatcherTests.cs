using FluentAssertions;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Retrieval;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Retrieval;

[Trait("Category", "Database")]
public class FuzzyMatcherTests : IDisposable
{
    private readonly IQmdDatabase db;

    public FuzzyMatcherTests()
    {
        this.db = TestDbHelper.CreateInMemoryDb();
        this.SeedDocs();
    }

    public void Dispose() => this.db.Dispose();

    private void SeedDocs()
    {
        var repo = new DocumentRepository(this.db);
        void Seed(string path)
        {
            var hash = ContentHasher.HashContent(path);
            repo.InsertContent(hash, $"Content of {path}", "2025-01-01");
            repo.InsertDocument("docs", path, path, hash, "2025-01-01", "2025-01-01");
        }
        Seed("readme.md");
        Seed("readme.txt");
        Seed("guide.md");
        Seed("api.md");
    }

    [Fact]
    public void Levenshtein_IdenticalStrings()
    {
        FuzzyMatcher.Levenshtein("hello", "hello").Should().Be(0);
    }

    [Fact]
    public void Levenshtein_SingleEdit()
    {
        FuzzyMatcher.Levenshtein("hello", "helo").Should().Be(1);
        FuzzyMatcher.Levenshtein("cat", "bat").Should().Be(1);
    }

    [Fact]
    public void Levenshtein_EmptyStrings()
    {
        FuzzyMatcher.Levenshtein("", "hello").Should().Be(5);
        FuzzyMatcher.Levenshtein("hello", "").Should().Be(5);
        FuzzyMatcher.Levenshtein("", "").Should().Be(0);
    }

    [Fact]
    public void FindSimilarFiles_FindsCloseMatches()
    {
        var similar = FuzzyMatcher.FindSimilarFiles(this.db, "readmi.md", maxDistance: 2);
        similar.Should().Contain("readme.md");
    }

    [Fact]
    public void FindSimilarFiles_RespectsLimit()
    {
        var similar = FuzzyMatcher.FindSimilarFiles(this.db, "readme", maxDistance: 10, limit: 2);
        similar.Should().HaveCountLessThanOrEqualTo(2);
    }

    [Fact]
    public void FindSimilarFiles_RespectsMaxDistance()
    {
        // Set maxDistance=1, verify tight filtering
        using var db = TestDbHelper.CreateInMemoryDb();

        var repo = new DocumentRepository(db);
        void Seed(string path)
        {
            var hash = ContentHasher.HashContent(path);
            repo.InsertContent(hash, $"Content of {path}", "2025-01-01");
            repo.InsertDocument("docs", path, path, hash, "2025-01-01", "2025-01-01");
        }
        Seed("abc.md");
        Seed("xyz.md");

        var similar = FuzzyMatcher.FindSimilarFiles(db, "abc.md", maxDistance: 1, limit: 5);
        similar.Should().Contain("abc.md");
        similar.Should().NotContain("xyz.md");
    }

    [Fact]
    public void MatchFilesByGlob_MatchesPatterns()
    {
        // Verify *.md matches markdown files
        using var db = TestDbHelper.CreateInMemoryDb();

        var repo = new DocumentRepository(db);
        void Seed(string collection, string path, string content)
        {
            var hash = ContentHasher.HashContent(content);
            repo.InsertContent(hash, content, "2025-01-01");
            repo.InsertDocument(collection, path, "Title", hash, "2025-01-01", "2025-01-01");
        }

        Seed("docs", "journals/2024-01.md", "January journal");
        Seed("docs", "journals/2024-02.md", "February journal");
        Seed("docs", "docs/readme.md", "Readme content");

        var matches = GlobMatcher.MatchFilesByGlob(db, "journals/*.md");
        matches.Should().HaveCount(2);
        matches.Should().AllSatisfy(m => m.DisplayPath.Should().StartWith("journals/"));
    }
}
