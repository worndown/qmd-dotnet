using System.Text.RegularExpressions;

namespace Qmd.Core.Paths;

/// <summary>
/// Filesystem path utilities for QMD.
/// Supports Unix, Windows, and Git Bash (/c/Users -> C:/Users) path formats.
/// All output paths are normalized to forward slashes for consistency with virtual paths.
/// </summary>
public static class QmdPaths
{
    private static bool _productionMode;

    public static string HomeDir()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// Check if a path is absolute.
    /// Supports Unix paths (/path), Windows native (C:\ or C:/),
    /// and Git Bash (/c/path, /D/path — drives C-Z only).
    /// </summary>
    public static bool IsAbsolutePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        // Unix absolute path or Git Bash path (/c/Users)
        if (path[0] == '/') return true;

        // Windows native: C:\ or C:/ or just C:
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
            return true;

        return false;
    }

    /// <summary>
    /// Detect if a path is a Git Bash style path like /c/ or /c/Users (drives C-Z).
    /// Requires path[2] == '/' to distinguish from Unix paths like /cache.
    /// </summary>
    private static bool IsGitBashPath(string path)
    {
        if (path.Length < 3 || path[0] != '/' || path[2] != '/')
            return false;
        var drive = path[1];
        return drive is (>= 'c' and <= 'z') or (>= 'C' and <= 'Z');
    }

    /// <summary>
    /// Extract Windows drive letter from a Git Bash path.
    /// Returns the drive prefix (e.g., "C:") and the remaining path (e.g., "/Users/name").
    /// </summary>
    private static (string Drive, string Remainder) ExtractGitBashDrive(string path)
    {
        var drive = char.ToUpper(path[1]) + ":";
        return (drive, path[2..]);
    }

    /// <summary>
    /// Normalize path separators to forward slashes.
    /// </summary>
    public static string NormalizePathSeparators(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>
    /// Get the relative path from a prefix.
    /// Returns null if path is not under prefix, empty string if path equals prefix.
    /// </summary>
    public static string? GetRelativePathFromPrefix(string path, string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return null;

        var normalizedPath = NormalizePathSeparators(path);
        var normalizedPrefix = NormalizePathSeparators(prefix);

        // Ensure prefix ends with /
        var prefixWithSlash = normalizedPrefix.EndsWith('/')
            ? normalizedPrefix
            : normalizedPrefix + '/';

        // Exact match
        if (normalizedPath == normalizedPrefix)
            return "";

        // Check if path starts with prefix/
        if (normalizedPath.StartsWith(prefixWithSlash))
            return normalizedPath[prefixWithSlash.Length..];

        return null;
    }

    /// <summary>
    /// Cross-platform path resolver. Normalizes output to forward slashes.
    /// Handles Windows drive letters and ./../ navigation.
    /// </summary>
    public static string Resolve(params string[] paths)
    {
        if (paths.Length == 0)
            throw new ArgumentException("resolve: at least one path segment is required");

        // Normalize all paths to forward slashes
        var normalizedPaths = paths.Select(NormalizePathSeparators).ToArray();

        string result;
        string windowsDrive = "";

        var firstPath = normalizedPaths[0];

        if (IsAbsolutePath(firstPath))
        {
            result = firstPath;

            // Extract Windows drive letter if present
            if (firstPath.Length >= 2 && char.IsLetter(firstPath[0]) && firstPath[1] == ':')
            {
                windowsDrive = firstPath[..2];
                result = firstPath[2..];
            }
            else if (IsGitBashPath(firstPath))
            {
                // Git Bash style: /c/ → C: (drives C-Z)
                var (drive, remainder) = ExtractGitBashDrive(firstPath);
                windowsDrive = drive;
                result = remainder;
            }
        }
        else
        {
            // Start with current directory
            var pwd = NormalizePathSeparators(GetPwd());

            if (pwd.Length >= 2 && char.IsLetter(pwd[0]) && pwd[1] == ':')
            {
                windowsDrive = pwd[..2];
                result = pwd[2..] + '/' + firstPath;
            }
            else
            {
                result = pwd + '/' + firstPath;
            }
        }

        // Process remaining paths
        for (int i = 1; i < normalizedPaths.Length; i++)
        {
            var p = normalizedPaths[i];
            if (IsAbsolutePath(p))
            {
                result = p;
                if (p.Length >= 2 && char.IsLetter(p[0]) && p[1] == ':')
                {
                    windowsDrive = p[..2];
                    result = p[2..];
                }
                else if (IsGitBashPath(p))
                {
                    var (drive, remainder) = ExtractGitBashDrive(p);
                    windowsDrive = drive;
                    result = remainder;
                }
                else
                {
                    windowsDrive = "";
                }
            }
            else
            {
                result = result + '/' + p;
            }
        }

        // Normalize . and .. components
        var parts = result.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>();
        foreach (var part in parts)
        {
            if (part == "..")
            {
                if (normalized.Count > 0)
                    normalized.RemoveAt(normalized.Count - 1);
            }
            else if (part != ".")
            {
                normalized.Add(part);
            }
        }

        var finalPath = '/' + string.Join('/', normalized);

        if (!string.IsNullOrEmpty(windowsDrive))
            return windowsDrive + finalPath;

        return finalPath;
    }

    /// <summary>
    /// Get the current working directory.
    /// </summary>
    public static string GetPwd()
    {
        return Environment.GetEnvironmentVariable("PWD") ?? Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Resolve a path to its real (canonical) form. Falls back to Resolve() on error.
    /// </summary>
    public static string GetRealPath(string path)
    {
        try
        {
            return NormalizePathSeparators(Path.GetFullPath(path));
        }
        catch
        {
            return Resolve(path);
        }
    }

    /// <summary>
    /// Get the QMD cache/data directory (%LOCALAPPDATA%\qmd\). Creates it if needed.
    /// </summary>
    public static string GetCacheDir()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var qmdCacheDir = Path.Combine(localAppData, "qmd");
        try { Directory.CreateDirectory(qmdCacheDir); } catch { }
        return NormalizePathSeparators(qmdCacheDir);
    }

    /// <summary>Path to the MCP daemon PID file.</summary>
    public static string GetMcpPidPath() => GetCacheDir() + "/mcp.pid";

    /// <summary>Path to the MCP daemon log file.</summary>
    public static string GetMcpLogPath() => GetCacheDir() + "/mcp.log";

    /// <summary>
    /// Get the default database path. Uses %LOCALAPPDATA%\qmd\{indexName}.sqlite.
    /// Respects INDEX_PATH env var override.
    /// </summary>
    public static string GetDefaultDbPath(string indexName = "index")
    {
        var envPath = Environment.GetEnvironmentVariable("INDEX_PATH");
        if (!string.IsNullOrEmpty(envPath))
            return envPath;

        if (!_productionMode)
        {
            throw new InvalidOperationException(
                "Database path not set. Tests must set INDEX_PATH env var or use createStore() with explicit path. " +
                "This prevents tests from accidentally writing to the global index.");
        }

        return GetCacheDir() + $"/{indexName}.sqlite";
    }

    public static void EnableProductionMode() => _productionMode = true;

    /// <summary>Reset production mode — only for testing.</summary>
    public static void ResetProductionModeForTesting() => _productionMode = false;
}
