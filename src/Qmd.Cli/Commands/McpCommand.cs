using System.CommandLine;
using System.Diagnostics;
using Qmd.Core.Paths;
using Qmd.Mcp;

namespace Qmd.Cli.Commands;

public static class McpCommand
{
    public static Command Create()
    {
        var httpOpt = new Option<bool>("--http") { Description = "Use HTTP transport instead of stdio" };
        var portOpt = new Option<int>("--port") { Description = "HTTP port", DefaultValueFactory = _ => 8181 };
        var daemonOpt = new Option<bool>("--daemon") { Description = "Run as background daemon" };

        var cmd = new Command("mcp", "Start MCP server") { httpOpt, portOpt, daemonOpt };

        // Subcommand: qmd mcp stop
        var stopCmd = new Command("stop", "Stop the MCP daemon");
        stopCmd.SetAction(parseResult =>
        {
            var pidPath = QmdPaths.GetMcpPidPath();
            if (!File.Exists(pidPath))
            {
                Console.WriteLine("Not running (no PID file).");
                return;
            }

            var pid = int.Parse(File.ReadAllText(pidPath).Trim());
            try
            {
                var proc = Process.GetProcessById(pid);
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(5000);
                }
                File.Delete(pidPath);
                Console.WriteLine($"Stopped QMD MCP server (PID {pid}).");
            }
            catch (ArgumentException)
            {
                // Process not found — stale PID file
                File.Delete(pidPath);
                Console.WriteLine("Cleaned up stale PID file (server was not running).");
            }
        });
        cmd.Subcommands.Add(stopCmd);

        cmd.SetAction(async (ParseResult parseResult, CancellationToken token) =>
        {
            var http = parseResult.GetValue(httpOpt);
            var port = parseResult.GetValue(portOpt);
            var daemon = parseResult.GetValue(daemonOpt);

            if (!http && !daemon)
            {
                // Stdio transport — default for Claude Desktop / agent integration
                await using var store = await CliHelper.CreateStoreAsync();
                await McpServerSetup.RunStdioAsync(store);
                return;
            }

            if (daemon && !http)
            {
                Console.Error.WriteLine("--daemon requires --http");
                Environment.Exit(1);
            }

            if (daemon)
            {
                // Daemon mode — spawn background process
                var pidPath = QmdPaths.GetMcpPidPath();
                var logPath = QmdPaths.GetMcpLogPath();

                // Guard: check if already running
                if (File.Exists(pidPath))
                {
                    var existingPid = int.Parse(File.ReadAllText(pidPath).Trim());
                    try
                    {
                        var existing = Process.GetProcessById(existingPid);
                        if (!existing.HasExited)
                        {
                            Console.Error.WriteLine($"Already running (PID {existingPid}). Run 'qmd mcp stop' first.");
                            Environment.Exit(1);
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Stale PID file — continue
                    }
                }

                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    Console.Error.WriteLine("Cannot determine executable path for daemon spawn.");
                    Environment.Exit(1);
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"mcp --http --port {port}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                startInfo.Environment["QMD_LOG_FILE"] = logPath;

                var child = Process.Start(startInfo);
                if (child == null)
                {
                    Console.Error.WriteLine("Failed to start daemon process.");
                    Environment.Exit(1);
                }

                File.WriteAllText(pidPath, child.Id.ToString());
                Console.WriteLine($"Started on http://localhost:{port}/mcp (PID {child.Id})");
                Console.WriteLine($"Logs: {logPath}");
                Environment.Exit(0);
            }
            else
            {
                // Foreground HTTP mode
                await using var store = await CliHelper.CreateStoreAsync();
                await McpServerSetup.RunHttpAsync(store, port);
            }
        });

        return cmd;
    }
}
