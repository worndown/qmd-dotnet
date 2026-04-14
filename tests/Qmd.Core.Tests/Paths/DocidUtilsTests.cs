using FluentAssertions;
using Qmd.Core.Paths;

namespace Qmd.Core.Tests.Paths;

[Trait("Category", "Unit")]
public class DocidUtilsTests
{
    [Fact]
    public void GetDocid_ReturnsFirst6Chars()
    {
        DocidUtils.GetDocid("abc123def456").Should().Be("abc123");
        DocidUtils.GetDocid("000000anything").Should().Be("000000");
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
        DocidUtils.Normalize(input).Should().Be(expected);
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
    public void IsDocid_ValidatesCorrectly(string input, bool expected)
    {
        DocidUtils.IsDocid(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_UppercaseHexPreserved()
    {
        DocidUtils.Normalize("#ABC123").Should().Be("ABC123");
        DocidUtils.Normalize("\"ABC123\"").Should().Be("ABC123");
    }

    [Fact]
    public void Normalize_MismatchedQuotesPreserved()
    {
        // Mismatched quotes should NOT be stripped
        DocidUtils.Normalize("\"abc123'").Should().Be("\"abc123'");
        DocidUtils.Normalize("'abc123\"").Should().Be("'abc123\"");
    }

    [Fact]
    public void IsDocid_RejectsFilePaths()
    {
        DocidUtils.IsDocid("/path/to/file.md").Should().BeFalse();
        DocidUtils.IsDocid("path/to/file.md").Should().BeFalse();
        DocidUtils.IsDocid("qmd://collection/file.md").Should().BeFalse();
    }

    [Fact]
    public void IsDocid_RejectsHexWithExtension()
    {
        DocidUtils.IsDocid("abc123.md").Should().BeFalse();
    }
}
