using FluentAssertions;
using Qmd.Core.Paths;

namespace Qmd.Core.Tests.Paths;

[Trait("Category", "Unit")]
public class QmdPathsTests : IDisposable
{
    private readonly string? _originalPwd;

    public QmdPathsTests()
    {
        _originalPwd = Environment.GetEnvironmentVariable("PWD");
    }

    public void Dispose()
    {
        if (_originalPwd != null)
            Environment.SetEnvironmentVariable("PWD", _originalPwd);
        else
            Environment.SetEnvironmentVariable("PWD", null);
    }

    private static void MockPwd(string path)
    {
        Environment.SetEnvironmentVariable("PWD", path);
    }

    [Theory]
    [InlineData("/path/to/file", true)]
    [InlineData("/", true)]
    [InlineData("/home/user/documents", true)]
    [InlineData("/usr/local/bin", true)]
    [InlineData("/a", true)]
    [InlineData("/1/", true)]
    public void IsAbsolutePath_UnixAbsolute_ReturnsTrue(string path, bool expected)
    {
        QmdPaths.IsAbsolutePath(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("path/to/file")]
    [InlineData("./path/to/file")]
    [InlineData("../path/to/file")]
    [InlineData("./file")]
    [InlineData("../file")]
    [InlineData("file.txt")]
    public void IsAbsolutePath_Relative_ReturnsFalse(string path)
    {
        QmdPaths.IsAbsolutePath(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("C:/path/to/file")]
    [InlineData("C:/")]
    [InlineData("D:/Users/Documents")]
    [InlineData("Z:/")]
    [InlineData("c:/lowercase")]
    public void IsAbsolutePath_WindowsForwardSlash_ReturnsTrue(string path)
    {
        QmdPaths.IsAbsolutePath(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("C:\\path\\to\\file")]
    [InlineData("C:\\")]
    [InlineData("D:\\Users\\Documents")]
    [InlineData("Z:\\")]
    [InlineData("c:\\lowercase")]
    public void IsAbsolutePath_WindowsBackslash_ReturnsTrue(string path)
    {
        QmdPaths.IsAbsolutePath(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("path\\to\\file")]
    [InlineData(".\\path\\to\\file")]
    [InlineData("..\\path\\to\\file")]
    public void IsAbsolutePath_WindowsRelative_ReturnsFalse(string path)
    {
        QmdPaths.IsAbsolutePath(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("C:", true)]   // Drive letter only
    [InlineData("C", false)]   // Just a letter
    [InlineData(":", false)]
    public void IsAbsolutePath_EdgeCases(string path, bool expected)
    {
        QmdPaths.IsAbsolutePath(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("/c/Users/name/file", true)]
    [InlineData("/C/Users/name/file", true)]
    [InlineData("/d/Projects", true)]
    [InlineData("/D/Projects", true)]
    [InlineData("/z/", true)]
    public void IsAbsolutePath_GitBashPaths_ReturnsTrue(string path, bool expected)
    {
        QmdPaths.IsAbsolutePath(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("C:\\Users\\name\\file.txt", "C:/Users/name/file.txt")]
    [InlineData("D:\\Projects\\qmd\\src", "D:/Projects/qmd/src")]
    [InlineData("\\path\\to\\file", "/path/to/file")]
    public void NormalizePathSeparators_WindowsPaths(string input, string expected)
    {
        QmdPaths.NormalizePathSeparators(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("C:\\Users/name\\file.txt", "C:/Users/name/file.txt")]
    [InlineData("path\\to/file/here", "path/to/file/here")]
    public void NormalizePathSeparators_MixedSeparators(string input, string expected)
    {
        QmdPaths.NormalizePathSeparators(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("/path/to/file", "/path/to/file")]
    [InlineData("/usr/local/bin", "/usr/local/bin")]
    [InlineData("relative/path", "relative/path")]
    public void NormalizePathSeparators_UnixPathsUnchanged(string input, string expected)
    {
        QmdPaths.NormalizePathSeparators(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("path\\\\to\\\\file", "path//to//file")]
    [InlineData("C:\\\\Users\\\\name", "C://Users//name")]
    public void NormalizePathSeparators_MultipleBackslashes(string input, string expected)
    {
        QmdPaths.NormalizePathSeparators(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("\\", "/")]
    [InlineData("\\\\", "//")]
    [InlineData("file.txt", "file.txt")]
    public void NormalizePathSeparators_EdgeCases(string input, string expected)
    {
        QmdPaths.NormalizePathSeparators(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("/home/user", "/home/user", "")]
    [InlineData("C:/Users/name", "C:/Users/name", "")]
    [InlineData("/path", "/path", "")]
    public void GetRelativePathFromPrefix_ExactMatch_ReturnsEmpty(string path, string prefix, string expected)
    {
        QmdPaths.GetRelativePathFromPrefix(path, prefix).Should().Be(expected);
    }

    [Theory]
    [InlineData("/home/user/documents", "/home/user", "documents")]
    [InlineData("/home/user/documents/file.txt", "/home/user", "documents/file.txt")]
    [InlineData("C:/Users/name/Documents/file.txt", "C:/Users/name", "Documents/file.txt")]
    public void GetRelativePathFromPrefix_PathUnderPrefix_ReturnsRelative(string path, string prefix, string expected)
    {
        QmdPaths.GetRelativePathFromPrefix(path, prefix).Should().Be(expected);
    }

    [Theory]
    [InlineData("/home/other", "/home/user")]
    [InlineData("/usr/local", "/home/user")]
    [InlineData("C:/Users/other", "D:/Users")]
    public void GetRelativePathFromPrefix_PathNotUnderPrefix_ReturnsNull(string path, string prefix)
    {
        QmdPaths.GetRelativePathFromPrefix(path, prefix).Should().BeNull();
    }

    [Fact]
    public void GetRelativePathFromPrefix_WindowsBackslashes_Normalized()
    {
        QmdPaths.GetRelativePathFromPrefix("C:\\Users\\name\\Documents", "C:\\Users\\name")
            .Should().Be("Documents");
        QmdPaths.GetRelativePathFromPrefix("C:\\Users\\name\\Documents\\file.txt", "C:/Users/name")
            .Should().Be("Documents/file.txt");
    }

    [Theory]
    [InlineData("/home/user/documents", "/home/user/", "documents")]
    [InlineData("C:/Users/name/Documents", "C:/Users/name/", "Documents")]
    public void GetRelativePathFromPrefix_TrailingSlash(string path, string prefix, string expected)
    {
        QmdPaths.GetRelativePathFromPrefix(path, prefix).Should().Be(expected);
    }

    [Fact]
    public void GetRelativePathFromPrefix_EdgeCases()
    {
        QmdPaths.GetRelativePathFromPrefix("/path/to/file", "").Should().BeNull();
        QmdPaths.GetRelativePathFromPrefix("/home/username", "/home/user").Should().BeNull();
        QmdPaths.GetRelativePathFromPrefix("/home/user", "/").Should().Be("home/user");
    }

    [Fact]
    public void Resolve_UnixRelativePaths()
    {
        MockPwd("/home/user");
        QmdPaths.Resolve("/base", "relative").Should().Be("/base/relative");
        QmdPaths.Resolve("/base", "a/b/c").Should().Be("/base/a/b/c");
        QmdPaths.Resolve("/home", "user/documents").Should().Be("/home/user/documents");
    }

    [Fact]
    public void Resolve_UnixAbsolutePaths()
    {
        MockPwd("/home/user");
        QmdPaths.Resolve("/base", "/absolute").Should().Be("/absolute");
        QmdPaths.Resolve("/home/user", "/usr/local").Should().Be("/usr/local");
        QmdPaths.Resolve("/any", "/").Should().Be("/");
    }

    [Fact]
    public void Resolve_UnixDotDot()
    {
        MockPwd("/home/user");
        QmdPaths.Resolve("/base", "../other").Should().Be("/other");
        QmdPaths.Resolve("/base/sub", "..").Should().Be("/base");
        QmdPaths.Resolve("/base", "./file").Should().Be("/base/file");
        QmdPaths.Resolve("/base/a/b", "../../c").Should().Be("/base/c");
    }

    [Fact]
    public void Resolve_UnixMultipleSegments()
    {
        MockPwd("/home/user");
        QmdPaths.Resolve("/a", "b", "c").Should().Be("/a/b/c");
        QmdPaths.Resolve("/a", "b", "../c").Should().Be("/a/c");
        QmdPaths.Resolve("/a", "b", "/c").Should().Be("/c");
    }

    [Fact]
    public void Resolve_UnixRelativeUsesPwd()
    {
        MockPwd("/home/user");
        QmdPaths.Resolve("relative").Should().Be("/home/user/relative");
        QmdPaths.Resolve("a/b/c").Should().Be("/home/user/a/b/c");
        QmdPaths.Resolve("./file").Should().Be("/home/user/file");
    }

    [Fact]
    public void Resolve_UnixAbsoluteAlone()
    {
        MockPwd("/home/user");
        QmdPaths.Resolve("/absolute/path").Should().Be("/absolute/path");
        QmdPaths.Resolve("/").Should().Be("/");
    }

    [Fact]
    public void Resolve_WindowsRelativePaths()
    {
        MockPwd("C:/Users/name");
        QmdPaths.Resolve("C:/base", "relative").Should().Be("C:/base/relative");
        QmdPaths.Resolve("C:/base", "a/b/c").Should().Be("C:/base/a/b/c");
        QmdPaths.Resolve("D:/Projects", "qmd/src").Should().Be("D:/Projects/qmd/src");
    }

    [Fact]
    public void Resolve_WindowsAbsolutePaths()
    {
        MockPwd("C:/Users/name");
        QmdPaths.Resolve("C:/base", "D:/other").Should().Be("D:/other");
        QmdPaths.Resolve("C:/Users", "C:/Program Files").Should().Be("C:/Program Files");
        QmdPaths.Resolve("D:/any", "E:/other").Should().Be("E:/other");
    }

    [Fact]
    public void Resolve_WindowsBackslashes()
    {
        MockPwd("C:/Users/name");
        QmdPaths.Resolve("C:\\base", "relative").Should().Be("C:/base/relative");
        QmdPaths.Resolve("C:\\Users\\name", "Documents").Should().Be("C:/Users/name/Documents");
        QmdPaths.Resolve("C:\\base", "a\\b\\c").Should().Be("C:/base/a/b/c");
    }

    [Fact]
    public void Resolve_WindowsDotDot()
    {
        MockPwd("C:/Users/name");
        QmdPaths.Resolve("C:/base", "../other").Should().Be("C:/other");
        QmdPaths.Resolve("C:/base/sub", "..").Should().Be("C:/base");
        QmdPaths.Resolve("C:/base", "./file").Should().Be("C:/base/file");
        QmdPaths.Resolve("C:/base/a/b", "../../c").Should().Be("C:/base/c");
    }

    [Fact]
    public void Resolve_WindowsMultipleSegments()
    {
        MockPwd("C:/Users/name");
        QmdPaths.Resolve("C:/a", "b", "c").Should().Be("C:/a/b/c");
        QmdPaths.Resolve("C:/a", "b", "../c").Should().Be("C:/a/c");
        QmdPaths.Resolve("C:/a", "b", "D:/c").Should().Be("D:/c");
    }

    [Fact]
    public void Resolve_WindowsRelativeUsesPwd()
    {
        MockPwd("C:/Users/name");
        QmdPaths.Resolve("relative").Should().Be("C:/Users/name/relative");
        QmdPaths.Resolve("a/b/c").Should().Be("C:/Users/name/a/b/c");
        QmdPaths.Resolve(".\\file").Should().Be("C:/Users/name/file");
    }

    [Fact]
    public void Resolve_WindowsDriveLetterOnly()
    {
        MockPwd("C:/Users/name");
        QmdPaths.Resolve("C:").Should().Be("C:/");
        QmdPaths.Resolve("D:").Should().Be("D:/");
    }

    [Fact]
    public void Resolve_GitBashToWindowsConversion()
    {
        QmdPaths.Resolve("/c/Users/name").Should().Be("C:/Users/name");
        QmdPaths.Resolve("/C/Users/name").Should().Be("C:/Users/name");
        QmdPaths.Resolve("/d/Projects").Should().Be("D:/Projects");
        QmdPaths.Resolve("/D/Projects").Should().Be("D:/Projects");
    }

    [Fact]
    public void Resolve_GitBashWithRelativePaths()
    {
        QmdPaths.Resolve("/c/base", "relative").Should().Be("C:/base/relative");
        QmdPaths.Resolve("/d/Projects", "qmd/src").Should().Be("D:/Projects/qmd/src");
    }

    [Fact]
    public void Resolve_GitBashWithDotDot()
    {
        QmdPaths.Resolve("/c/base", "../other").Should().Be("C:/other");
        QmdPaths.Resolve("/c/base/sub", "..").Should().Be("C:/base");
        QmdPaths.Resolve("/c/base", "./file").Should().Be("C:/base/file");
    }

    [Fact]
    public void Resolve_GitBashMultipleSegments()
    {
        QmdPaths.Resolve("/c/a", "b", "c").Should().Be("C:/a/b/c");
        QmdPaths.Resolve("/c/a", "b", "/d/c").Should().Be("D:/c");
    }

    [Fact]
    public void Resolve_EmptySegmentsFiltered()
    {
        MockPwd("/home/user");
        QmdPaths.Resolve("/base", "", "file").Should().Be("/base/file");
        QmdPaths.Resolve("C:/base", "", "file").Should().Be("C:/base/file");
    }

    [Fact]
    public void Resolve_MultipleConsecutiveSlashes()
    {
        MockPwd("/home/user");
        QmdPaths.Resolve("/base//path///file").Should().Be("/base/path/file");
        QmdPaths.Resolve("C:/base//path///file").Should().Be("C:/base/path/file");
    }

    [Fact]
    public void Resolve_TrailingSlashes()
    {
        MockPwd("/home/user");
        QmdPaths.Resolve("/base/", "file").Should().Be("/base/file");
        QmdPaths.Resolve("C:/base/", "file").Should().Be("C:/base/file");
    }

    [Fact]
    public void Resolve_ComplexDotDotNavigation()
    {
        MockPwd("/home/user");
        QmdPaths.Resolve("/a/b/c/d", "../../../e").Should().Be("/a/e");
        QmdPaths.Resolve("C:/a/b/c/d", "../../../e").Should().Be("C:/a/e");
    }

    [Fact]
    public void Resolve_TooManyDotDot_StopsAtRoot()
    {
        MockPwd("/home/user");
        QmdPaths.Resolve("/base", "../../../../other").Should().Be("/other");
        QmdPaths.Resolve("C:/base", "../../../../other").Should().Be("C:/other");
    }

    [Fact]
    public void Resolve_MixedUnixAndWindows()
    {
        MockPwd("C:/Users/name");
        QmdPaths.Resolve("/unix/path").Should().Be("/unix/path");
        QmdPaths.Resolve("relative").Should().Be("C:/Users/name/relative");
    }

    [Fact]
    public void Resolve_NoArguments_Throws()
    {
        var act = () => QmdPaths.Resolve();
        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least one path segment*");
    }

    [Fact]
    public void GetDefaultDbPath_RespectsIndexPathEnv()
    {
        var original = Environment.GetEnvironmentVariable("INDEX_PATH");
        try
        {
            Environment.SetEnvironmentVariable("INDEX_PATH", "C:/custom/path.sqlite");
            QmdPaths.GetDefaultDbPath().Should().Be("C:/custom/path.sqlite");
        }
        finally
        {
            Environment.SetEnvironmentVariable("INDEX_PATH", original);
        }
    }

    [Fact]
    public void GetDefaultDbPath_ThrowsInNonProductionMode()
    {
        var original = Environment.GetEnvironmentVariable("INDEX_PATH");
        try
        {
            Environment.SetEnvironmentVariable("INDEX_PATH", null);
            QmdPaths.ResetProductionModeForTesting();
            var act = () => QmdPaths.GetDefaultDbPath();
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Database path not set*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("INDEX_PATH", original);
        }
    }
}
