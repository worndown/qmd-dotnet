namespace Qmd.Cli.Progress;

/// <summary>
/// OSC 9;4 terminal progress sequences (supported in iTerm, Kitty, Windows Terminal).
/// All writes go to stderr and are gated on TTY detection.
/// </summary>
public static class OscProgress
{
    internal static bool IsTty => !Console.IsErrorRedirected;

    public static void Set(int percent)
    {
        if (IsTty)
            Console.Error.Write($"\x1b]9;4;1;{percent}\x07");
    }

    public static void Clear()
    {
        if (IsTty)
            Console.Error.Write("\x1b]9;4;0\x07");
    }

    public static void Indeterminate()
    {
        if (IsTty)
            Console.Error.Write("\x1b]9;4;3\x07");
    }

    public static void Error()
    {
        if (IsTty)
            Console.Error.Write("\x1b]9;4;2\x07");
    }

    // Build the OSC string without writing (useful for testing)
    internal static string BuildSet(int percent) => $"\x1b]9;4;1;{percent}\x07";
    internal static string BuildClear() => "\x1b]9;4;0\x07";
    internal static string BuildIndeterminate() => "\x1b]9;4;3\x07";
    internal static string BuildError() => "\x1b]9;4;2\x07";
}

/// <summary>
/// Cursor visibility helpers. Writes to stderr, gated on TTY detection.
/// </summary>
public static class CursorHelper
{
    public static void Hide()
    {
        if (OscProgress.IsTty)
            Console.Error.Write("\x1b[?25l");
    }

    public static void Show()
    {
        if (OscProgress.IsTty)
            Console.Error.Write("\x1b[?25h");
    }
}

/// <summary>
/// Formatting utilities for progress display: progress bar, ETA, byte sizes.
/// </summary>
public static class ProgressFormatting
{
    /// <summary>
    /// Render a visual progress bar using block characters.
    /// </summary>
    /// <param name="percent">Completion percentage (0-100).</param>
    /// <param name="width">Total bar width in characters.</param>
    /// <returns>A string like "████████░░░░░░░░░░░░░░░░░░░░░░".</returns>
    public static string RenderProgressBar(double percent, int width = 30)
    {
        var clamped = Math.Max(0, Math.Min(100, percent));
        var filled = (int)Math.Round(clamped / 100.0 * width);
        var empty = width - filled;
        return new string('\u2588', filled) + new string('\u2591', empty);
    }

    /// <summary>
    /// Format seconds into human-readable ETA: "42s", "1m 30s", "2h 1m".
    /// </summary>
    public static string FormatEta(double seconds)
    {
        if (seconds < 60)
            return $"{Math.Round(seconds)}s";
        if (seconds < 3600)
            return $"{Math.Floor(seconds / 60)}m {Math.Round(seconds % 60)}s";
        return $"{Math.Floor(seconds / 3600)}h {Math.Floor(seconds % 3600 / 60)}m";
    }

    /// <summary>
    /// Format a byte count into human-readable size: "500 B", "1.5 KB", "3.4 MB".
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}
