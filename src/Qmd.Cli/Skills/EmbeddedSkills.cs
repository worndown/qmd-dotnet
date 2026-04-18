using System.Reflection;

namespace Qmd.Cli.Skills;

internal record EmbeddedSkillFile(string RelativePath, string Content);

internal static class EmbeddedSkills
{
    private static readonly (string ResourceName, string RelativePath)[] Mapping =
    [
        ("Qmd.Cli.Claude.qmd_skill.md", "SKILL.md"),
        ("Qmd.Cli.Claude.mcp_setup.md", "references/mcp-setup.md"),
    ];

    private static readonly Lazy<Dictionary<string, string>> Cache = new(() =>
        Mapping.ToDictionary(m => m.ResourceName, m => ReadResource(m.ResourceName)));

    public static List<EmbeddedSkillFile> GetEmbeddedQmdSkillFiles()
    {
        return Mapping
            .Select(m => new EmbeddedSkillFile(m.RelativePath, Cache.Value[m.ResourceName]))
            .ToList();
    }

    public static string GetEmbeddedQmdSkillContent()
    {
        return Cache.Value[Mapping[0].ResourceName];
    }

    private static string ReadResource(string name)
    {
        var assembly = typeof(EmbeddedSkills).Assembly;
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{name}' is missing. " +
                $"Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
