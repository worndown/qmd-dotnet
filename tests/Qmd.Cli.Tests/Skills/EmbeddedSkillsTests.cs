using FluentAssertions;
using Qmd.Cli.Skills;

namespace Qmd.Cli.Tests.Skills;

public class EmbeddedSkillsTests
{
    [Fact]
    public void GetEmbeddedQmdSkillFiles_ReturnsTwoFiles()
    {
        var files = EmbeddedSkills.GetEmbeddedQmdSkillFiles();
        files.Should().HaveCount(2);
        files.Select(f => f.RelativePath).Should().Contain("SKILL.md");
        files.Select(f => f.RelativePath).Should().Contain("references/mcp-setup.md");
    }

    [Fact]
    public void GetEmbeddedQmdSkillContent_StartsWithYamlFrontmatter()
    {
        var content = EmbeddedSkills.GetEmbeddedQmdSkillContent();
        content.Should().NotBeNullOrEmpty();
        content.Should().StartWith("---");
    }

    [Fact]
    public void GetEmbeddedQmdSkillContent_ContainsNameQmd()
    {
        var content = EmbeddedSkills.GetEmbeddedQmdSkillContent();
        content.Should().Contain("name: qmd");
    }

    [Fact]
    public void GetEmbeddedQmdSkillFiles_ReferencesFileContainsMcpServerSetup()
    {
        var files = EmbeddedSkills.GetEmbeddedQmdSkillFiles();
        var referencesFile = files.First(f => f.RelativePath == "references/mcp-setup.md");
        referencesFile.Content.Should().Contain("MCP Server Setup");
    }

    [Fact]
    public void GetEmbeddedQmdSkillFiles_AllFilesHaveContent()
    {
        var files = EmbeddedSkills.GetEmbeddedQmdSkillFiles();
        foreach (var file in files)
        {
            file.Content.Should().NotBeNullOrEmpty($"file {file.RelativePath} should have content");
        }
    }
}
