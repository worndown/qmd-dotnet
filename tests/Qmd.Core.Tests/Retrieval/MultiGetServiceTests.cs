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
    private readonly IQmdDatabase db;
    private readonly DocumentRepository docRepo;
    private const string CollectionName = "testcol";
    private const string CollectionPath = "/path";

    public MultiGetServiceTests()
    {
        this.db = TestDbHelper.CreateInMemoryDb();
        this.docRepo = new DocumentRepository(this.db);
        this.SeedCollection();
    }

    public void Dispose() => this.db.Dispose();

    private void SeedCollection()
    {
        var config = new CollectionConfig
        {
            Collections = new()
            {
                [CollectionName] = new Collection { Path = CollectionPath }
            }
        };
        new ConfigSyncService(this.db).SyncToDb(config);
    }

    private void InsertDoc(string displayPath, string body = "# Test\n\nDefault content.", string title = "Test")
    {
        var hash = ContentHasher.HashContent(body);
        this.docRepo.InsertContent(hash, body, "2025-01-01");
        this.docRepo.InsertDocument(CollectionName, displayPath, title, hash, "2025-01-01", "2025-01-01");
    }

    private MultiGetService CreateService()
    {
        var contextResolver = new ContextResolverService(this.db);
        var fuzzyMatcher = new FuzzyMatcherService(this.db);
        var docFinder = new DocumentFinderService(this.db, fuzzyMatcher, contextResolver);
        return new MultiGetService(this.db, docFinder, contextResolver);
    }

    [Fact]
    public void FindDocuments_FindsByGlobPattern()
    {
        this.InsertDoc("journals/2024-01.md", "# Jan 2024", "Jan");
        this.InsertDoc("journals/2024-02.md", "# Feb 2024", "Feb");
        this.InsertDoc("other/file.md", "# Other", "Other");

        var (docs, errors) = this.CreateService().FindDocuments("journals/2024-*.md");
        errors.Should().BeEmpty();
        docs.Should().HaveCount(2);
    }

    [Fact]
    public void FindDocuments_FindsByCommaSeparatedList()
    {
        this.InsertDoc("doc1.md", "# Doc 1", "Doc1");
        this.InsertDoc("doc2.md", "# Doc 2", "Doc2");

        var (docs, errors) = this.CreateService().FindDocuments("doc1.md, doc2.md");
        errors.Should().BeEmpty();
        docs.Should().HaveCount(2);
    }

    [Fact]
    public void FindDocuments_ReportsErrorsForNotFoundFiles()
    {
        this.InsertDoc("doc1.md", "# Doc 1", "Doc1");

        var (docs, errors) = this.CreateService().FindDocuments("doc1.md, nonexistent.md");
        docs.Should().HaveCount(1);
        errors.Should().HaveCount(1);
        errors[0].Should().Contain("not found", Exactly.Once());
    }

    [Fact]
    public void FindDocuments_SkipsLargeFiles()
    {
        var largeBody = new string('x', 20000); // 20KB
        this.InsertDoc("large.md", largeBody, "Large");

        var (docs, errors) = this.CreateService().FindDocuments("large.md", maxBytes: 10000);
        docs.Should().HaveCount(1);
        docs[0].Skipped.Should().BeTrue();
        docs[0].SkipReason.Should().Contain("too large");
    }

    [Fact]
    public void FindDocuments_IncludesBodyWhenRequested()
    {
        this.InsertDoc("doc1.md", "The content", "Doc1");

        var (docs, _) = this.CreateService().FindDocuments("doc1.md", includeBody: true);
        docs.Should().HaveCount(1);
        docs[0].Skipped.Should().BeFalse();
        docs[0].Doc.Body.Should().Be("The content");
    }

    [Fact(Skip = "DotNet.Glob 3.x does not support brace expansion patterns like {a,b}.md")]
    public void FindDocuments_SupportsBraceExpansion()
    {
        this.InsertDoc("doc1.md", "# Doc 1", "Doc1");
        this.InsertDoc("doc2.md", "# Doc 2", "Doc2");
        this.InsertDoc("doc3.md", "# Doc 3", "Doc3");

        // Brace expansion: {doc1,doc2}.md — DotNet.Glob does not support this
        var (docs, errors) = this.CreateService().FindDocuments("{doc1,doc2}.md");
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

        this.InsertDoc("file1.md", body1, "Doc One");
        this.InsertDoc("file2.md", body2, "Doc Two");

        var docid1 = $"#{hash1[..6]}";
        var docid2 = $"#{hash2[..6]}";

        var (docs, errors) = this.CreateService().FindDocuments($"{docid1}, {docid2}");
        errors.Should().BeEmpty();
        docs.Should().HaveCount(2);
    }
}
