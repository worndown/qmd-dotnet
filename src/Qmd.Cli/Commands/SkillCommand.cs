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
            CliContext.Console.WriteLine("QMD Skill (embedded)");
            CliContext.Console.WriteLine();
            var content = EmbeddedSkills.GetEmbeddedQmdSkillContent();
            CliContext.Console.Write(content.EndsWith('\n') ? content : content + "\n");
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
            CliContext.Console.WriteLine($"Installed QMD skill to {installDir}");

            var claudeLinkPath = SkillInstaller.GetClaudeSkillLinkPath(global);
            if (!SkillInstaller.ShouldCreateClaudeSymlink(yes, _ =>
            {
                if (CliContext.Console.IsInputRedirected)
                {
                    CliContext.Console.WriteLine($"Tip: create a Claude symlink manually at {claudeLinkPath}");
                    return false;
                }
                CliContext.Console.Write($"Create a symlink in {claudeLinkPath}? [y/N] ");
                var answer = CliContext.Console.ReadLine()?.Trim().ToLowerInvariant();
                return answer is "y" or "yes";
            }))
                return;

            try
            {
                var linked = SkillInstaller.EnsureClaudeSymlink(claudeLinkPath, installDir, force);
                if (linked)
                    CliContext.Console.WriteLine($"Linked Claude skill at {claudeLinkPath}");
                else
                    CliContext.Console.WriteLine($"Claude already sees the skill via {Path.GetDirectoryName(claudeLinkPath)}");
            }
            catch (UnauthorizedAccessException ex)
            {
                CliContext.Console.WriteErrorLine(ex.Message);
            }
        });

        cmd.Subcommands.Add(showCmd);
        cmd.Subcommands.Add(installCmd);
        return cmd;
    }
}
