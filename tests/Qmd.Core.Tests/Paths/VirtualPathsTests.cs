using FluentAssertions;
using Qmd.Core.Paths;

namespace Qmd.Core.Tests.Paths;

public class VirtualPathsTests
{
    [Theory]
    [InlineData("qmd://docs/readme.md", true)]
    [InlineData("qmd://collection/path", true)]
    [InlineData("//docs/readme.md", true)]
    [InlineData("/tmp/file.md", false)]
    [InlineData("relative/path", false)]
    [InlineData("collection/path.md", false)]
    public void IsVirtualPath_DetectsCorrectly(string path, bool expected)
    {
        VirtualPaths.IsVirtualPath(path).Should().Be(expected);
    }

    [Fact]
    public void Parse_ValidVirtualPath()
    {
        var result = VirtualPaths.Parse("qmd://docs/readme.md");
        result.Should().NotBeNull();
        result!.CollectionName.Should().Be("docs");
        result.Path.Should().Be("readme.md");
    }

    [Fact]
    public void Parse_CollectionRoot()
    {
        var result = VirtualPaths.Parse("qmd://docs/");
        result.Should().NotBeNull();
        result!.CollectionName.Should().Be("docs");
        result.Path.Should().BeEmpty();
    }

    [Fact]
    public void Parse_CollectionRootNoSlash()
    {
        var result = VirtualPaths.Parse("qmd://docs");
        result.Should().NotBeNull();
        result!.CollectionName.Should().Be("docs");
        result.Path.Should().BeEmpty();
    }

    [Fact]
    public void Parse_InvalidPath_ReturnsNull()
    {
        VirtualPaths.Parse("relative/path").Should().BeNull();
        VirtualPaths.Parse("/tmp/file").Should().BeNull();
    }

    [Fact]
    public void Build_CreatesVirtualPath()
    {
        VirtualPaths.Build("docs", "readme.md").Should().Be("qmd://docs/readme.md");
        VirtualPaths.Build("mylib", "src/main.ts").Should().Be("qmd://mylib/src/main.ts");
    }

    [Theory]
    [InlineData("qmd://docs/readme.md", "qmd://docs/readme.md")]
    [InlineData("docs/readme.md", "docs/readme.md")]  // Non-virtual unchanged
    [InlineData("qmd:////docs/readme.md", "qmd://docs/readme.md")]  // Extra slashes
    [InlineData("//docs/readme.md", "qmd://docs/readme.md")]  // Missing prefix
    public void Normalize_HandlesVariousFormats(string input, string expected)
    {
        VirtualPaths.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_BareCollectionPathPreserved()
    {
        // Bare paths without qmd:// or // prefix are NOT converted
        VirtualPaths.Normalize("collection/path.md").Should().Be("collection/path.md");
        VirtualPaths.Normalize("journals/2025-01-01.md").Should().Be("journals/2025-01-01.md");
    }

    [Fact]
    public void Normalize_AbsoluteFilesystemPathsPreserved()
    {
        VirtualPaths.Normalize("/Users/test/file.md").Should().Be("/Users/test/file.md");
        VirtualPaths.Normalize("/absolute/path/file.md").Should().Be("/absolute/path/file.md");
    }

    [Fact]
    public void Normalize_HomeRelativePathsPreserved()
    {
        VirtualPaths.Normalize("~/Documents/file.md").Should().Be("~/Documents/file.md");
    }

    [Fact]
    public void Normalize_DocidFormatPreserved()
    {
        VirtualPaths.Normalize("#abc123").Should().Be("#abc123");
        VirtualPaths.Normalize("#def456").Should().Be("#def456");
    }

    [Fact]
    public void IsVirtualPath_RejectsBareCollectionPath()
    {
        VirtualPaths.IsVirtualPath("collection/path.md").Should().BeFalse();
        VirtualPaths.IsVirtualPath("journals/2025-01-01.md").Should().BeFalse();
        VirtualPaths.IsVirtualPath("archive/subfolder/file.md").Should().BeFalse();
    }

    [Fact]
    public void IsVirtualPath_RejectsDocid()
    {
        VirtualPaths.IsVirtualPath("#abc123").Should().BeFalse();
        VirtualPaths.IsVirtualPath("#def456").Should().BeFalse();
    }

    [Fact]
    public void IsVirtualPath_RejectsPathsWithoutSlashes()
    {
        VirtualPaths.IsVirtualPath("file.md").Should().BeFalse();
        VirtualPaths.IsVirtualPath("document").Should().BeFalse();
    }

    [Fact]
    public void Parse_NestedDirectories()
    {
        var result = VirtualPaths.Parse("qmd://archive/subfolder/file.md");
        result.Should().NotBeNull();
        result!.CollectionName.Should().Be("archive");
        result.Path.Should().Be("subfolder/file.md");
    }

    [Fact]
    public void Parse_NestedDeepPath()
    {
        var result = VirtualPaths.Parse("qmd://docs/sub/dir/file.md");
        result.Should().NotBeNull();
        result!.CollectionName.Should().Be("docs");
        result.Path.Should().Be("sub/dir/file.md");
    }
}
