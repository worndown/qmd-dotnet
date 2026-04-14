using FluentAssertions;
using Qmd.Core.Search;

namespace Qmd.Core.Tests.Search;

[Trait("Category", "Unit")]
public class Fts5QueryBuilderTests
{

    [Fact]
    public void PlainTerm_PrefixMatch()
    {
        Fts5QueryBuilder.BuildFTS5Query("hello").Should().Be("\"hello\"*");
    }

    [Fact]
    public void MultiplePlainTerms_JoinedWithAnd()
    {
        Fts5QueryBuilder.BuildFTS5Query("hello world").Should().Be("\"hello\"* AND \"world\"*");
    }

    [Fact]
    public void PlainTerms_Lowercased()
    {
        Fts5QueryBuilder.BuildFTS5Query("Hello World").Should().Be("\"hello\"* AND \"world\"*");
    }

    [Fact]
    public void PlainTerms_SpecialCharsStripped()
    {
        Fts5QueryBuilder.BuildFTS5Query("hello! world@").Should().Be("\"hello\"* AND \"world\"*");
    }

    [Fact]
    public void QuotedPhrase_ExactMatch()
    {
        Fts5QueryBuilder.BuildFTS5Query("\"machine learning\"").Should().Be("\"machine learning\"");
    }

    [Fact]
    public void QuotedPhrase_MixedWithPlain()
    {
        Fts5QueryBuilder.BuildFTS5Query("\"machine learning\" algorithms").Should()
            .Be("\"machine learning\" AND \"algorithms\"*");
    }

    [Fact]
    public void Negation_SingleTerm()
    {
        Fts5QueryBuilder.BuildFTS5Query("performance -sports").Should()
            .Be("\"performance\"* NOT \"sports\"*");
    }

    [Fact]
    public void Negation_MultiplePlus()
    {
        Fts5QueryBuilder.BuildFTS5Query("performance -sports -games").Should()
            .Be("\"performance\"* NOT \"sports\"* NOT \"games\"*");
    }

    [Fact]
    public void Negation_QuotedPhrase()
    {
        Fts5QueryBuilder.BuildFTS5Query("search -\"machine learning\"").Should()
            .Be("\"search\"* NOT \"machine learning\"");
    }

    [Fact]
    public void Negation_OnlyNegative_ReturnsNull()
    {
        Fts5QueryBuilder.BuildFTS5Query("-sports").Should().BeNull();
        Fts5QueryBuilder.BuildFTS5Query("-sports -games").Should().BeNull();
    }

    [Fact]
    public void Hyphenated_PhraseMatch()
    {
        Fts5QueryBuilder.BuildFTS5Query("multi-agent").Should().Be("\"multi agent\"");
    }

    [Fact]
    public void Hyphenated_MixedWithPlain()
    {
        Fts5QueryBuilder.BuildFTS5Query("multi-agent memory").Should()
            .Be("\"multi agent\" AND \"memory\"*");
    }

    [Fact]
    public void Hyphenated_NumericalCode()
    {
        Fts5QueryBuilder.BuildFTS5Query("DEC-0054").Should().Be("\"dec 0054\"");
    }

    [Fact]
    public void Hyphenated_Negated()
    {
        Fts5QueryBuilder.BuildFTS5Query("search -multi-agent").Should()
            .Be("\"search\"* NOT \"multi agent\"");
    }

    [Fact]
    public void EmptyQuery_ReturnsNull()
    {
        Fts5QueryBuilder.BuildFTS5Query("").Should().BeNull();
        Fts5QueryBuilder.BuildFTS5Query("   ").Should().BeNull();
    }

    [Fact]
    public void OnlySpecialChars_ReturnsNull()
    {
        Fts5QueryBuilder.BuildFTS5Query("!@#$%").Should().BeNull();
    }

    [Fact]
    public void PreservesUnderscores()
    {
        Fts5QueryBuilder.BuildFTS5Query("my_variable").Should().Be("\"my_variable\"*");
    }

    [Fact]
    public void PreservesApostrophes()
    {
        Fts5QueryBuilder.BuildFTS5Query("don't").Should().Be("\"don't\"*");
    }

    [Theory]
    [InlineData("multi-agent", true)]
    [InlineData("DEC-0054", true)]
    [InlineData("gpt-4", true)]
    [InlineData("hello", false)]
    [InlineData("-hello", false)]
    [InlineData("hello-", false)]
    [InlineData("a-b-c", true)]
    public void IsHyphenatedToken_Detects(string token, bool expected)
    {
        Fts5QueryBuilder.IsHyphenatedToken(token).Should().Be(expected);
    }

    [Fact]
    public void ValidateSemanticQuery_RejectsNegation()
    {
        QueryValidator.ValidateSemanticQuery("-term").Should().NotBeNull();
        QueryValidator.ValidateSemanticQuery("normal query").Should().BeNull();
    }

    [Fact]
    public void ValidateLexQuery_RejectsNewlines()
    {
        QueryValidator.ValidateLexQuery("line1\nline2").Should().NotBeNull();
        QueryValidator.ValidateLexQuery("single line").Should().BeNull();
    }

    [Fact]
    public void ValidateLexQuery_RejectsUnmatchedQuotes()
    {
        QueryValidator.ValidateLexQuery("hello \"world").Should().NotBeNull();
        QueryValidator.ValidateLexQuery("\"hello\" \"world\"").Should().BeNull();
    }
}
