using FluentAssertions;
using Qmd.Core.Paths;

namespace Qmd.Core.Tests.Paths;

[Trait("Category", "Unit")]
public class DocIdUtilsTests
{
    [Fact]
    public void GetDocId_ReturnsFirst6Chars()
    {
        DocIdUtils.GetDocId("abc123def456").Should().Be("abc123");
        DocIdUtils.GetDocId("000000anything").Should().Be("000000");
    }

    [Theory]
    [InlineData("123456", "123456")]
    [InlineData("#123456", "123456")]
    [InlineData("\"123456\"", "123456")]
    [InlineData("'123456'", "123456")]
    [InlineData("  123456  ", "123456")]
    [InlineData("#abc123", "abc123")]
    public void Normalize_HandlesVariousFormats(string input, string expected)
    {
        DocIdUtils.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("123456", true)]
    [InlineData("#123456", true)]
    [InlineData("abcdef", true)]
    [InlineData("ABCDEF", true)]
    [InlineData("abc123def456", true)]  // Longer than 6 is fine
    [InlineData("12345", false)]        // Too short
    [InlineData("bad-id", false)]       // Non-hex
    [InlineData("ghijkl", false)]       // Non-hex letters
    public void IsDocId_ValidatesCorrectly(string input, bool expected)
    {
        DocIdUtils.IsDocId(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_UppercaseHexPreserved()
    {
        DocIdUtils.Normalize("#ABC123").Should().Be("ABC123");
        DocIdUtils.Normalize("\"ABC123\"").Should().Be("ABC123");
    }

    [Fact]
    public void Normalize_MismatchedQuotesPreserved()
    {
        // Mismatched quotes should NOT be stripped
        DocIdUtils.Normalize("\"abc123'").Should().Be("\"abc123'");
        DocIdUtils.Normalize("'abc123\"").Should().Be("'abc123\"");
    }

    [Fact]
    public void IsDocId_RejectsFilePaths()
    {
        DocIdUtils.IsDocId("/path/to/file.md").Should().BeFalse();
        DocIdUtils.IsDocId("path/to/file.md").Should().BeFalse();
        DocIdUtils.IsDocId("qmd://collection/file.md").Should().BeFalse();
    }

    [Fact]
    public void IsDocId_RejectsHexWithExtension()
    {
        DocIdUtils.IsDocId("abc123.md").Should().BeFalse();
    }
}
