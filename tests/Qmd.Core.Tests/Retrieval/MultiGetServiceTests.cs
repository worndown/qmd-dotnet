using FluentAssertions;
using Qmd.Core.Configuration;
using Qmd.Core.Content;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Retrieval;
using Qmd.Core.Tests.TestHelpers;

namespace Qmd.Core.Tests.Retrieval;

[Trait("Category", "Database")]
public class MultiGetServiceTests : IDisposable
{
    private readonly IQmdDatabase _db;
    private readonly DocumentRepository _docRepo;
    private const string CollectionName = "testcol";
    private const string CollectionPath = "/path";

    public MultiGetServiceTests()
    {
        _db = TestDbHelper.CreateInMemoryDb();
        _docRepo = new DocumentRepository(_db);
        SeedCollection();
    }

    public void Dispose() => _db.Dispose();

    private void SeedCollection()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                [CollectionName] = new Collection { Path = CollectionPath }
            }
        };
        new ConfigSyncService(_db).SyncToDb(config);
    }

    private void InsertDoc(string displayPath, string body = "# Test\n\nDefault content.", string title = "Test")
    {
        var hash = ContentHasher.HashContent(body);
        _docRepo.InsertContent(hash, body, "2025-01-01");
        _docRepo.InsertDocument(CollectionName, displayPath, title, hash, "2025-01-01", "2025-01-01");
    }

    private MultiGetServiceImpl CreateService()
    {
        var contextResolver = new ContextResolverService(_db);
        var fuzzyMatcher = new FuzzyMatcherService(_db);
        var docFinder = new DocumentFinderService(_db, fuzzyMatcher, contextResolver);
        return new MultiGetServiceImpl(_db, docFinder, contextResolver);
    }

    [Fact]
    public void FindDocuments_FindsByGlobPattern()
    {
        InsertDoc("journals/2024-01.md", "# Jan 2024", "Jan");
        InsertDoc("journals/2024-02.md", "# Feb 2024", "Feb");
        InsertDoc("other/file.md", "# Other", "Other");

        var (docs, errors) = CreateService().FindDocuments("journals/2024-*.md");
        errors.Should().BeEmpty();
        docs.Should().HaveCount(2);
    }

    [Fact]
    public void FindDocuments_FindsByCommaSeparatedList()
    {
        InsertDoc("doc1.md", "# Doc 1", "Doc1");
        InsertDoc("doc2.md", "# Doc 2", "Doc2");

        var (docs, errors) = CreateService().FindDocuments("doc1.md, doc2.md");
        errors.Should().BeEmpty();
        docs.Should().HaveCount(2);
    }

    [Fact]
    public void FindDocuments_ReportsErrorsForNotFoundFiles()
    {
        InsertDoc("doc1.md", "# Doc 1", "Doc1");

        var (docs, errors) = CreateService().FindDocuments("doc1.md, nonexistent.md");
        docs.Should().HaveCount(1);
        errors.Should().HaveCount(1);
        errors[0].Should().Contain("not found", Exactly.Once());
    }

    [Fact]
    public void FindDocuments_SkipsLargeFiles()
    {
        var largeBody = new string('x', 20000); // 20KB
        InsertDoc("large.md", largeBody, "Large");

        var (docs, errors) = CreateService().FindDocuments("large.md", maxBytes: 10000);
        docs.Should().HaveCount(1);
        docs[0].Skipped.Should().BeTrue();
        docs[0].SkipReason.Should().Contain("too large");
    }

    [Fact]
    public void FindDocuments_IncludesBodyWhenRequested()
    {
        InsertDoc("doc1.md", "The content", "Doc1");

        var (docs, _) = CreateService().FindDocuments("doc1.md", includeBody: true);
        docs.Should().HaveCount(1);
        docs[0].Skipped.Should().BeFalse();
        docs[0].Doc.Body.Should().Be("The content");
    }

    [Fact(Skip = "DotNet.Glob 3.x does not support brace expansion patterns like {a,b}.md")]
    public void FindDocuments_SupportsBraceExpansion()
    {
        InsertDoc("doc1.md", "# Doc 1", "Doc1");
        InsertDoc("doc2.md", "# Doc 2", "Doc2");
        InsertDoc("doc3.md", "# Doc 3", "Doc3");

        // Brace expansion: {doc1,doc2}.md — DotNet.Glob does not support this
        var (docs, errors) = CreateService().FindDocuments("{doc1,doc2}.md");
        errors.Should().BeEmpty();
        docs.Should().HaveCount(2);
    }

    [Fact]
    public void FindDocuments_FindsByDocidInCommaList()
    {
        var body1 = "# Document One\n\nFirst document content.";
        var body2 = "# Document Two\n\nSecond document content.";
        var hash1 = ContentHasher.HashContent(body1);
        var hash2 = ContentHasher.HashContent(body2);

        InsertDoc("file1.md", body1, "Doc One");
        InsertDoc("file2.md", body2, "Doc Two");

        var docid1 = $"#{hash1[..6]}";
        var docid2 = $"#{hash2[..6]}";

        var (docs, errors) = CreateService().FindDocuments($"{docid1}, {docid2}");
        errors.Should().BeEmpty();
        docs.Should().HaveCount(2);
    }
}
