using FluentAssertions;
using Qmd.Cli.Progress;

namespace Qmd.Cli.Tests.Progress;

public class TerminalProgressTests
{

    [Theory]
    [InlineData(30, "30s")]
    [InlineData(0, "0s")]
    [InlineData(59, "59s")]
    public void FormatEta_SecondsOnly(double input, string expected)
    {
        ProgressFormatting.FormatEta(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(90, "1m 30s")]
    [InlineData(60, "1m 0s")]
    [InlineData(150, "2m 30s")]
    public void FormatEta_MinutesAndSeconds(double input, string expected)
    {
        ProgressFormatting.FormatEta(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(3700, "1h 1m")]
    [InlineData(3600, "1h 0m")]
    [InlineData(7260, "2h 1m")]
    public void FormatEta_HoursAndMinutes(double input, string expected)
    {
        ProgressFormatting.FormatEta(input).Should().Be(expected);
    }

    [Fact]
    public void RenderProgressBar_ZeroPercent_AllEmpty()
    {
        var bar = ProgressFormatting.RenderProgressBar(0, 10);
        bar.Should().Be(new string('\u2591', 10));
        bar.Length.Should().Be(10);
    }

    [Fact]
    public void RenderProgressBar_HundredPercent_AllFilled()
    {
        var bar = ProgressFormatting.RenderProgressBar(100, 10);
        bar.Should().Be(new string('\u2588', 10));
        bar.Length.Should().Be(10);
    }

    [Fact]
    public void RenderProgressBar_FiftyPercent_HalfAndHalf()
    {
        var bar = ProgressFormatting.RenderProgressBar(50, 10);
        bar.Should().Be(new string('\u2588', 5) + new string('\u2591', 5));
        bar.Length.Should().Be(10);
    }

    [Fact]
    public void RenderProgressBar_DefaultWidth_Is30()
    {
        var bar = ProgressFormatting.RenderProgressBar(50);
        bar.Length.Should().Be(30);
    }

    [Fact]
    public void RenderProgressBar_ClampsAbove100()
    {
        var bar = ProgressFormatting.RenderProgressBar(150, 10);
        bar.Should().Be(new string('\u2588', 10));
    }

    [Fact]
    public void RenderProgressBar_ClampsBelow0()
    {
        var bar = ProgressFormatting.RenderProgressBar(-10, 10);
        bar.Should().Be(new string('\u2591', 10));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(500, "500 B")]
    [InlineData(1023, "1023 B")]
    public void FormatBytes_Bytes(long input, string expected)
    {
        ProgressFormatting.FormatBytes(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1500, "1.5 KB")]
    [InlineData(10240, "10.0 KB")]
    public void FormatBytes_Kilobytes(long input, string expected)
    {
        ProgressFormatting.FormatBytes(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1500000, "1.4 MB")]
    public void FormatBytes_Megabytes(long input, string expected)
    {
        ProgressFormatting.FormatBytes(input).Should().Be(expected);
    }

    [Fact]
    public void FormatBytes_Gigabytes()
    {
        ProgressFormatting.FormatBytes(1_073_741_824).Should().Be("1.0 GB");
    }

    [Fact]
    public void OscProgress_Set_ProducesCorrectEscapeSequence()
    {
        OscProgress.BuildSet(50).Should().Be("\x1b]9;4;1;50\x07");
    }

    [Fact]
    public void OscProgress_Set_ZeroPercent()
    {
        OscProgress.BuildSet(0).Should().Be("\x1b]9;4;1;0\x07");
    }

    [Fact]
    public void OscProgress_Set_HundredPercent()
    {
        OscProgress.BuildSet(100).Should().Be("\x1b]9;4;1;100\x07");
    }

    [Fact]
    public void OscProgress_Clear_ProducesCorrectEscapeSequence()
    {
        OscProgress.BuildClear().Should().Be("\x1b]9;4;0\x07");
    }

    [Fact]
    public void OscProgress_Indeterminate_ProducesCorrectEscapeSequence()
    {
        OscProgress.BuildIndeterminate().Should().Be("\x1b]9;4;3\x07");
    }

    [Fact]
    public void OscProgress_Error_ProducesCorrectEscapeSequence()
    {
        OscProgress.BuildError().Should().Be("\x1b]9;4;2\x07");
    }
}
