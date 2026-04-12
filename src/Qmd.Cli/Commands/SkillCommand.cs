using System.CommandLine;
using Qmd.Cli.Skills;

namespace Qmd.Cli.Commands;

public static class SkillCommand
{
    public static Command Create()
    {
        var cmd = new Command("skill", "Show or install the packaged QMD skill");

        // skill show
        var showCmd = new Command("show", "Print the embedded SKILL.md to stdout");
        showCmd.SetAction(parseResult =>
        {
            Console.WriteLine("QMD Skill (embedded)");
            Console.WriteLine();
            var content = EmbeddedSkills.GetEmbeddedQmdSkillContent();
            Console.Write(content.EndsWith('\n') ? content : content + "\n");
        });

        // skill install
        var globalOpt = new Option<bool>("--global") { Description = "Install to home directory instead of current directory" };
        var yesOpt = new Option<bool>("--yes") { Description = "Auto-confirm symlink creation" };
        var forceOpt = new Option<bool>("--force", "-f") { Description = "Overwrite existing install" };
        var installCmd = new Command("install", "Install the QMD skill files")
        {
            globalOpt, yesOpt, forceOpt
        };

        installCmd.SetAction(parseResult =>
        {
            var global = parseResult.GetValue(globalOpt);
            var yes = parseResult.GetValue(yesOpt);
            var force = parseResult.GetValue(forceOpt);

            var installDir = SkillInstaller.GetSkillInstallDir(global);
            SkillInstaller.WriteEmbeddedSkill(installDir, force);
            Console.WriteLine($"Installed QMD skill to {installDir}");

            var claudeLinkPath = SkillInstaller.GetClaudeSkillLinkPath(global);
            if (!SkillInstaller.ShouldCreateClaudeSymlink(yes, _ =>
            {
                if (Console.IsInputRedirected)
                {
                    Console.WriteLine($"Tip: create a Claude symlink manually at {claudeLinkPath}");
                    return false;
                }
                Console.Write($"Create a symlink in {claudeLinkPath}? [y/N] ");
                var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
                return answer is "y" or "yes";
            }))
                return;

            try
            {
                var linked = SkillInstaller.EnsureClaudeSymlink(claudeLinkPath, installDir, force);
                if (linked)
                    Console.WriteLine($"Linked Claude skill at {claudeLinkPath}");
                else
                    Console.WriteLine($"Claude already sees the skill via {Path.GetDirectoryName(claudeLinkPath)}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        });

        cmd.Subcommands.Add(showCmd);
        cmd.Subcommands.Add(installCmd);
        return cmd;
    }
}
