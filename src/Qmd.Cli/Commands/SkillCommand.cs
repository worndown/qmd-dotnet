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
            var options = new SkillInstallOptions(
                parseResult.GetValue(globalOpt),
                parseResult.GetValue(yesOpt),
                parseResult.GetValue(forceOpt));

            var result = SkillInstaller.Install(
                options,
                promptUser: linkPath =>
                {
                    if (CliContext.Console.IsInputRedirected)
                    {
                        CliContext.Console.WriteLine($"Tip: create a Claude symlink manually at {linkPath}");
                        return false;
                    }
                    CliContext.Console.Write($"Create a symlink in {linkPath}? [y/N] ");
                    var answer = CliContext.Console.ReadLine()?.Trim().ToLowerInvariant();
                    return answer is "y" or "yes";
                },
                onInstalled: installDir =>
                    CliContext.Console.WriteLine($"Installed QMD skill to {installDir}"));

            switch (result.Symlink)
            {
                case SymlinkOutcome.Created:
                    CliContext.Console.WriteLine($"Linked Claude skill at {result.ClaudeLinkPath}");
                    break;
                case SymlinkOutcome.AlreadyLinked:
                    CliContext.Console.WriteLine(
                        $"Claude already sees the skill via {Path.GetDirectoryName(result.ClaudeLinkPath)}");
                    break;
                case SymlinkOutcome.Failed:
                    CliContext.Console.WriteErrorLine(result.SymlinkError!);
                    break;
                case SymlinkOutcome.NotRequested:
                    break;
                case SymlinkOutcome.ClaudeNotDetected:
                    CliContext.Console.WriteLine(
                        $"Tip: Claude Code not detected. Install Claude Code, then run " +
                        $"`qmd skill install --yes` to create the symlink at {result.ClaudeLinkPath}");
                    break;
            }
        });

        cmd.Subcommands.Add(showCmd);
        cmd.Subcommands.Add(installCmd);
        return cmd;
    }
}
