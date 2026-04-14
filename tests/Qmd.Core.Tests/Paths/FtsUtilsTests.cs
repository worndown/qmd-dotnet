using FluentAssertions;
using Qmd.Core.Paths;

namespace Qmd.Core.Tests.Paths;

public class FtsUtilsTests
{
    [Theory]
    [InlineData("my_variable", "my_variable")]
    [InlineData("MAX_RETRIES", "max_retries")]
    [InlineData("__init__", "__init__")]
    public void SanitizeTerm_PreservesUnderscores(string input, string expected)
    {
        FtsUtils.SanitizeTerm(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("hello123", "hello123")]
    [InlineData("test", "test")]
    public void SanitizeTerm_PreservesAlphanumeric(string input, string expected)
    {
        FtsUtils.SanitizeTerm(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("don't", "don't")]
    [InlineData("it's", "it's")]
    public void SanitizeTerm_PreservesApostrophes(string input, string expected)
    {
        FtsUtils.SanitizeTerm(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("hello!", "hello")]
    [InlineData("test@value", "testvalue")]
    [InlineData("a.b", "ab")]
    public void SanitizeTerm_StripsPunctuation(string input, string expected)
    {
        FtsUtils.SanitizeTerm(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Hello", "hello")]
    [InlineData("MY_VAR", "my_var")]
    public void SanitizeTerm_Lowercases(string input, string expected)
    {
        FtsUtils.SanitizeTerm(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("café", "café")]
    [InlineData("日本語", "日本語")]
    public void SanitizeTerm_PreservesUnicode(string input, string expected)
    {
        FtsUtils.SanitizeTerm(input).Should().Be(expected);
    }
}
