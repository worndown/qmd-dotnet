namespace Qmd.Cli.Skills;

internal record SkillInstallOptions(bool Global, bool AutoYes, bool Force);

internal enum SymlinkOutcome
{
    NotRequested,
    ClaudeNotDetected,
    Created,
    AlreadyLinked,
    Failed,
}

internal record SkillInstallResult(
    string InstallDir,
    string ClaudeLinkPath,
    SymlinkOutcome Symlink,
    string? SymlinkError);

internal static class SkillInstaller
{
    public static SkillInstallResult Install(
        SkillInstallOptions options,
        Func<string, bool>? promptUser = null,
        Action<string>? onInstalled = null)
    {
        var installDir = GetSkillInstallDir(options.Global);
        WriteEmbeddedSkill(installDir, options.Force);
        onInstalled?.Invoke(installDir);

        var claudeLinkPath = GetClaudeSkillLinkPath(options.Global);

        if (!options.AutoYes && !IsClaudeCodeInstalled(options.Global))
            return new SkillInstallResult(installDir, claudeLinkPath, SymlinkOutcome.ClaudeNotDetected, null);

        if (!ShouldCreateClaudeSymlink(options.AutoYes, claudeLinkPath, promptUser))
            return new SkillInstallResult(installDir, claudeLinkPath, SymlinkOutcome.NotRequested, null);

        try
        {
            var linked = EnsureClaudeSymlink(claudeLinkPath, installDir, options.Force);
            return new SkillInstallResult(
                installDir,
                claudeLinkPath,
                linked ? SymlinkOutcome.Created : SymlinkOutcome.AlreadyLinked,
                null);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new SkillInstallResult(installDir, claudeLinkPath, SymlinkOutcome.Failed, ex.Message);
        }
    }

    public static string GetSkillInstallDir(bool global)
    {
        var root = global
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Directory.GetCurrentDirectory();
        return Path.Combine(root, ".agents", "skills", "qmd");
    }

    public static string GetClaudeSkillLinkPath(bool global)
    {
        var root = global
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Directory.GetCurrentDirectory();
        return Path.Combine(root, ".claude", "skills", "qmd");
    }

    public static bool IsClaudeCodeInstalled(bool global)
    {
        var root = global
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Directory.GetCurrentDirectory();
        return Directory.Exists(Path.Combine(root, ".claude"));
    }

    public static void WriteEmbeddedSkill(string targetDir, bool force)
    {
        if (Directory.Exists(targetDir) || File.Exists(targetDir))
        {
            if (!force)
                throw new InvalidOperationException(
                    $"Skill already exists: {targetDir} (use --force to replace it)");

            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, recursive: true);
            else
                File.Delete(targetDir);
        }

        Directory.CreateDirectory(targetDir);

        foreach (var file in EmbeddedSkills.GetEmbeddedQmdSkillFiles())
        {
            var destination = Path.Combine(targetDir, file.RelativePath);
            var dir = Path.GetDirectoryName(destination);
            if (dir != null)
                Directory.CreateDirectory(dir);
            File.WriteAllText(destination, file.Content);
        }
    }

    public static bool EnsureClaudeSymlink(string linkPath, string targetDir, bool force)
    {
        var parentDir = Path.GetDirectoryName(linkPath)!;

        // Loop detection: if the parent of the link resolves to the same dir as the
        // parent of the target, creating qmd -> qmd would loop.
        if (Directory.Exists(parentDir))
        {
            var resolvedTargetParent = Path.GetFullPath(Path.GetDirectoryName(targetDir)!);
            var resolvedLinkParent = Path.GetFullPath(parentDir);

            if (string.Equals(resolvedTargetParent, resolvedLinkParent, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        var linkTarget = Path.GetRelativePath(parentDir, targetDir);

        Directory.CreateDirectory(parentDir);

        if (Path.Exists(linkPath))
        {
            // Check if existing symlink already points to the right target
            var info = new FileInfo(linkPath);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var existingTarget = info.LinkTarget;
                if (existingTarget != null && NormalizeLinkTarget(existingTarget) == NormalizeLinkTarget(linkTarget))
                    return true;
            }

            if (!force)
                throw new InvalidOperationException(
                    $"Claude skill path already exists: {linkPath} (use --force to replace it)");

            if (Directory.Exists(linkPath))
                Directory.Delete(linkPath, recursive: true);
            else
                File.Delete(linkPath);
        }

        try
        {
            Directory.CreateSymbolicLink(linkPath, linkTarget);
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException(
                $"Cannot create symlink at {linkPath}. " +
                "On Windows, enable Developer Mode in Settings > For Developers, " +
                "or run as Administrator.");
        }

        return true;
    }

    public static bool ShouldCreateClaudeSymlink(bool autoYes, string linkPath, Func<string, bool>? promptUser = null)
    {
        if (autoYes)
            return true;

        return promptUser?.Invoke(linkPath) ?? false;
    }

    private static string NormalizeLinkTarget(string target)
    {
        return target.Replace('\\', '/').TrimEnd('/');
    }
}
