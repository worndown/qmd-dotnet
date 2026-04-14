using FluentAssertions;
using Qmd.Core.Content;

namespace Qmd.Core.Tests.Content;

[Trait("Category", "Unit")]
public class TitleExtractorTests
{
    [Fact]
    public void ExtractTitle_MarkdownH1()
    {
        TitleExtractor.ExtractTitle("# My Title\nSome content", "readme.md")
            .Should().Be("My Title");
    }

    [Fact]
    public void ExtractTitle_MarkdownH2()
    {
        TitleExtractor.ExtractTitle("## My Heading\nContent", "doc.md")
            .Should().Be("My Heading");
    }

    [Fact]
    public void ExtractTitle_SkipsGenericNotes()
    {
        var content = "# Notes\n## Actual Title\nContent";
        TitleExtractor.ExtractTitle(content, "meeting.md")
            .Should().Be("Actual Title");
    }

    [Fact]
    public void ExtractTitle_OrgTitle()
    {
        TitleExtractor.ExtractTitle("#+TITLE: Org Document\n* Heading", "doc.org")
            .Should().Be("Org Document");
    }

    [Fact]
    public void ExtractTitle_OrgHeading()
    {
        TitleExtractor.ExtractTitle("* First Heading\nContent", "doc.org")
            .Should().Be("First Heading");
    }

    [Fact]
    public void ExtractTitle_FallbackToFilename()
    {
        TitleExtractor.ExtractTitle("No headings here", "myfile.txt")
            .Should().Be("myfile");
    }

    [Fact]
    public void ExtractTitle_FallbackPathLastSegment()
    {
        TitleExtractor.ExtractTitle("No headings", "docs/api/reference.txt")
            .Should().Be("reference");
    }

    [Fact]
    public void ExtractTitle_HandlesEmojiHeading()
    {
        // "# 📝 Notes" should be skipped like plain "Notes"
        var content = "# 📝 Notes\n\n## Meeting Summary\n\nContent";
        TitleExtractor.ExtractTitle(content, "file.md")
            .Should().Be("Meeting Summary");
    }
}
