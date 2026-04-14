using FluentAssertions;
using Qmd.Cli.Skills;

namespace Qmd.Cli.Tests.Skills;

[Trait("Category", "Integration")]
public class SkillInstallerTests : IDisposable
{
    private readonly string _tempDir;

    public SkillInstallerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "qmd-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void WriteEmbeddedSkill_CreatesDirectoryStructure()
    {
        var targetDir = Path.Combine(_tempDir, "skills", "qmd");
        SkillInstaller.WriteEmbeddedSkill(targetDir, force: false);

        Directory.Exists(targetDir).Should().BeTrue();
        File.Exists(Path.Combine(targetDir, "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(targetDir, "references", "mcp-setup.md")).Should().BeTrue();
    }

    [Fact]
    public void WriteEmbeddedSkill_WritesCorrectContent()
    {
        var targetDir = Path.Combine(_tempDir, "skills", "qmd");
        SkillInstaller.WriteEmbeddedSkill(targetDir, force: false);

        var skillContent = File.ReadAllText(Path.Combine(targetDir, "SKILL.md"));
        skillContent.Should().StartWith("---");
        skillContent.Should().Contain("name: qmd");
    }

    [Fact]
    public void WriteEmbeddedSkill_ThrowsWhenExistsAndForceIsFalse()
    {
        var targetDir = Path.Combine(_tempDir, "skills", "qmd");
        SkillInstaller.WriteEmbeddedSkill(targetDir, force: false);

        var act = () => SkillInstaller.WriteEmbeddedSkill(targetDir, force: false);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already exists*--force*");
    }

    [Fact]
    public void WriteEmbeddedSkill_ReplacesWhenForceIsTrue()
    {
        var targetDir = Path.Combine(_tempDir, "skills", "qmd");
        SkillInstaller.WriteEmbeddedSkill(targetDir, force: false);

        // Add an extra file that should be removed after force reinstall
        File.WriteAllText(Path.Combine(targetDir, "extra.txt"), "should be removed");

        SkillInstaller.WriteEmbeddedSkill(targetDir, force: true);

        File.Exists(Path.Combine(targetDir, "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(targetDir, "extra.txt")).Should().BeFalse();
    }

    [Fact]
    public void GetSkillInstallDir_LocalReturnsCurrentDirBased()
    {
        var dir = SkillInstaller.GetSkillInstallDir(global: false);
        dir.Should().EndWith(Path.Combine(".agents", "skills", "qmd"));
        dir.Should().StartWith(Directory.GetCurrentDirectory());
    }

    [Fact]
    public void GetSkillInstallDir_GlobalReturnsHomeDirBased()
    {
        var dir = SkillInstaller.GetSkillInstallDir(global: true);
        dir.Should().EndWith(Path.Combine(".agents", "skills", "qmd"));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        dir.Should().StartWith(home);
    }

    [Fact]
    public void GetClaudeSkillLinkPath_LocalReturnsCurrentDirBased()
    {
        var path = SkillInstaller.GetClaudeSkillLinkPath(global: false);
        path.Should().EndWith(Path.Combine(".claude", "skills", "qmd"));
        path.Should().StartWith(Directory.GetCurrentDirectory());
    }

    [Fact]
    public void GetClaudeSkillLinkPath_GlobalReturnsHomeDirBased()
    {
        var path = SkillInstaller.GetClaudeSkillLinkPath(global: true);
        path.Should().EndWith(Path.Combine(".claude", "skills", "qmd"));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        path.Should().StartWith(home);
    }

    [Fact]
    public void EnsureClaudeSymlink_DetectsLoopWhenParentsMatch()
    {
        // If .claude/skills and .agents/skills resolve to the same directory,
        // creating the symlink would loop. EnsureClaudeSymlink should return false.
        var sharedDir = Path.Combine(_tempDir, "skills");
        Directory.CreateDirectory(sharedDir);

        var linkPath = Path.Combine(sharedDir, "qmd");
        var targetDir = Path.Combine(sharedDir, "qmd-target");
        // Parent of linkPath == parent of targetDir == sharedDir
        // This simulates the loop scenario where both resolve to the same parent.

        var result = SkillInstaller.EnsureClaudeSymlink(linkPath, targetDir, force: false);
        result.Should().BeFalse();
    }
}
