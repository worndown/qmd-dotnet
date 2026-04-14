using FluentAssertions;
using Qmd.Core.Paths;

namespace Qmd.Core.Tests.Paths;

[Trait("Category", "Unit")]
public class HandelizeTests
{
    [Theory]
    [InlineData("README.md", "readme.md")]
    [InlineData("MyFile.MD", "myfile.md")]
    public void Convert_ConvertsToLowercase(string input, string expected)
    {
        Handelize.Convert(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("a/b/c/d.md", "a/b/c/d.md")]
    [InlineData("docs/api/README.md", "docs/api/readme.md")]
    public void Convert_PreservesFolderStructure(string input, string expected)
    {
        Handelize.Convert(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("hello world.md", "hello-world.md")]
    [InlineData("file (1).md", "file-1.md")]
    [InlineData("foo@bar#baz.md", "foo-bar-baz.md")]
    public void Convert_ReplacesNonWordCharsWithDash(string input, string expected)
    {
        Handelize.Convert(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("hello   world.md", "hello-world.md")]
    [InlineData("foo---bar.md", "foo-bar.md")]
    [InlineData("a  -  b.md", "a-b.md")]
    public void Convert_CollapsesMultipleSpecialChars(string input, string expected)
    {
        Handelize.Convert(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("-hello-.md", "hello.md")]
    [InlineData("--test--.md", "test.md")]
    [InlineData("a/-b-/c.md", "a/b/c.md")]
    public void Convert_RemovesLeadingTrailingDashes(string input, string expected)
    {
        Handelize.Convert(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("foo___bar.md", "foo/bar.md")]
    [InlineData("notes___2025___january.md", "notes/2025/january.md")]
    [InlineData("a/b___c/d.md", "a/b/c/d.md")]
    public void Convert_TripleUnderscoreToFolder(string input, string expected)
    {
        Handelize.Convert(input).Should().Be(expected);
    }

    [Fact]
    public void Convert_ComplexMeetingNotes()
    {
        var result = Handelize.Convert(
            "Money Movement Licensing Review - 2025\uFF0F11\uFF0F19 10:25 EST - Notes by Gemini.md");
        result.Should().Be("money-movement-licensing-review-2025-11-19-10-25-est-notes-by-gemini.md");
        result.Should().NotContain(" ");
        result.Should().NotContain("\uFF0F");
        result.Should().NotContain(":");
    }

    [Theory]
    [InlineData("日本語.md", "日本語.md")]
    [InlineData("café-notes.md", "café-notes.md")]
    [InlineData("naïve.md", "naïve.md")]
    [InlineData("日本語-notes.md", "日本語-notes.md")]
    public void Convert_PreservesUnicodeLetters(string input, string expected)
    {
        Handelize.Convert(input).Should().Be(expected);
    }

    [Fact]
    public void Convert_CyrillicLowercase()
    {
        Handelize.Convert("Зоны и проекты.md").Should().Be("зоны-и-проекты.md");
    }

    [Fact]
    public void Convert_EmojiFilenames()
    {
        Handelize.Convert("\U0001F418.md").Should().Be("1f418.md");  // 🐘
        Handelize.Convert("\U0001F389.md").Should().Be("1f389.md");  // 🎉
        Handelize.Convert("notes \U0001F418.md").Should().Be("notes-1f418.md");
        Handelize.Convert("\U0001F418 elephant.md").Should().Be("1f418-elephant.md");
        Handelize.Convert("\U0001F418\U0001F389.md").Should().Be("1f418-1f389.md");
        Handelize.Convert("\U0001F418/notes.md").Should().Be("1f418/notes.md");
    }

    [Theory]
    [InlineData("meeting-2025-01-15.md", "meeting-2025-01-15.md")]
    [InlineData("call_10:30_AM.md", "call-10-30-am.md")]
    public void Convert_DatesAndTimes(string input, string expected)
    {
        Handelize.Convert(input).Should().Be(expected);
    }

    [Fact]
    public void Convert_DateInPath()
    {
        Handelize.Convert("notes 2025/01/15.md").Should().Be("notes-2025/01/15.md");
    }

    [Theory]
    [InlineData("PROJECT_ABC_v2.0.md", "project-abc-v2-0.md")]
    [InlineData("[WIP] Feature Request.md", "wip-feature-request.md")]
    [InlineData("(DRAFT) Proposal v1.md", "draft-proposal-v1.md")]
    public void Convert_SpecialProjectPatterns(string input, string expected)
    {
        Handelize.Convert(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("routes/api/auth/$.ts", "routes/api/auth/$.ts")]
    [InlineData("app/routes/$id.tsx", "app/routes/$id.tsx")]
    public void Convert_SymbolRouteFilenames(string input, string expected)
    {
        Handelize.Convert(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("a//b/c.md", "a/b/c.md")]
    [InlineData("/a/b/", "a/b")]
    [InlineData("///test///", "test")]
    public void Convert_FiltersEmptySegments(string input, string expected)
    {
        Handelize.Convert(input).Should().Be(expected);
    }

    [Fact]
    public void Convert_ThrowsForInvalidInputs()
    {
        var act1 = () => Handelize.Convert("");
        act1.Should().Throw<ArgumentException>().WithMessage("*path cannot be empty*");

        var act2 = () => Handelize.Convert("   ");
        act2.Should().Throw<ArgumentException>().WithMessage("*path cannot be empty*");

        var act3 = () => Handelize.Convert(".md");
        act3.Should().Throw<ArgumentException>().WithMessage("*no valid filename content*");

        var act4 = () => Handelize.Convert("___");
        act4.Should().Throw<ArgumentException>().WithMessage("*no valid filename content*");
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("1", "1")]
    [InlineData("a.md", "a.md")]
    public void Convert_MinimalValidInputs(string input, string expected)
    {
        Handelize.Convert(input).Should().Be(expected);
    }
}
