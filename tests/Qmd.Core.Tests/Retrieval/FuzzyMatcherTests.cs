using FluentAssertions;
using Qmd.Core.Configuration;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Retrieval;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Retrieval;

[Trait("Category", "Database")]
public class FuzzyMatcherTests : IDisposable
{
    private readonly IQmdDatabase _db;

    public FuzzyMatcherTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
        SeedDocs();
    }

    public void Dispose() => _db.Dispose();

    private void SeedDocs()
    {
        void Seed(string path)
        {
            var hash = ContentHasher.HashContent(path);
            ContentHasher.InsertContent(_db, hash, $"Content of {path}", "2025-01-01");
            DocumentOperations.InsertDocument(_db, "docs", path, path, hash, "2025-01-01", "2025-01-01");
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
        var similar = FuzzyMatcher.FindSimilarFiles(_db, "readmi.md", maxDistance: 2);
        similar.Should().Contain("readme.md");
    }

    [Fact]
    public void FindSimilarFiles_RespectsLimit()
    {
        var similar = FuzzyMatcher.FindSimilarFiles(_db, "readme", maxDistance: 10, limit: 2);
        similar.Should().HaveCountLessThanOrEqualTo(2);
    }

    [Fact]
    public void FindSimilarFiles_RespectsMaxDistance()
    {
        // Set maxDistance=1, verify tight filtering
        // "abc.md" vs "abc.md" = 0, "abc.md" vs "xyz.md" = 3
        // With maxDistance=1, only "abc.md" should appear (distance 0 to itself)
        using var db = TestDbHelper.CreateInMemoryDb();

        void Seed(string path)
        {
            var hash = ContentHasher.HashContent(path);
            ContentHasher.InsertContent(db, hash, $"Content of {path}", "2025-01-01");
            DocumentOperations.InsertDocument(db, "docs", path, path, hash, "2025-01-01", "2025-01-01");
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

        void Seed(string collection, string path, string content)
        {
            var hash = ContentHasher.HashContent(content);
            ContentHasher.InsertContent(db, hash, content, "2025-01-01");
            DocumentOperations.InsertDocument(db, collection, path, "Title", hash, "2025-01-01", "2025-01-01");
        }

        Seed("docs", "journals/2024-01.md", "January journal");
        Seed("docs", "journals/2024-02.md", "February journal");
        Seed("docs", "docs/readme.md", "Readme content");

        var matches = GlobMatcher.MatchFilesByGlob(db, "journals/*.md");
        matches.Should().HaveCount(2);
        matches.Should().AllSatisfy(m => m.DisplayPath.Should().StartWith("journals/"));
    }
}
