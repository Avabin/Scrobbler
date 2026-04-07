namespace Scrobbler.Cli.Commands;

using System.Diagnostics;
using ConsoleAppFramework;

/// <summary>
/// Install/uninstall the scrobbler daemon as a systemd user service.
/// </summary>
[RegisterCommands]
public class InstallCommands
{
    private static readonly string UnitDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "systemd", "user");

    private const string ServiceName = "scrobbler.service";

    /// <summary>
    /// Generate and install a systemd user unit file for the scrobbler daemon.
    /// </summary>
    /// <param name="execPath">Path to the scrbl daemon binary. Defaults to 'scrbl' on PATH.</param>
    [Command("install")]
    public async Task Install([Argument] string? execPath = null)
    {
        // Resolve daemon binary path
        execPath ??= await FindOnPath("scrbl");

        if (string.IsNullOrEmpty(execPath))
        {
            Console.WriteLine("Could not find 'scrbl' on PATH. Provide the path with -e.");
            return;
        }

        execPath = Path.GetFullPath(execPath);

        if (!File.Exists(execPath))
        {
            Console.WriteLine($"Binary not found: {execPath}");
            return;
        }

        var unit = $"""
            [Unit]
            Description=Last.fm Scrobbler Daemon
            After=graphical-session.target
            PartOf=graphical-session.target

            [Service]
            Type=notify
            ExecStart={execPath}
            Restart=on-failure
            RestartSec=5
            Environment=DOTNET_ENVIRONMENT=Production

            [Install]
            WantedBy=default.target
            """;

        Directory.CreateDirectory(UnitDir);
        var unitPath = Path.Combine(UnitDir, ServiceName);
        await File.WriteAllTextAsync(unitPath, unit);
        Console.WriteLine($"Unit file written to {unitPath}");

        Console.WriteLine("\nRun the following commands to enable and start the service:");
        Console.WriteLine($"  systemctl --user daemon-reload");
        Console.WriteLine($"  systemctl --user enable {ServiceName}");
        Console.WriteLine($"  systemctl --user start {ServiceName}");
        Console.WriteLine();
        Console.WriteLine("Useful commands:");
        Console.WriteLine($"  systemctl --user status {ServiceName}");
        Console.WriteLine($"  journalctl --user -u {ServiceName} -f");
    }

    /// <summary>
    /// Remove the systemd user unit file.
    /// </summary>
    [Command("uninstall")]
    public Task Uninstall()
    {
        var unitPath = Path.Combine(UnitDir, ServiceName);

        if (!File.Exists(unitPath))
        {
            Console.WriteLine("Service is not installed.");
            return Task.CompletedTask;
        }

        File.Delete(unitPath);
        Console.WriteLine($"Removed {unitPath}");

        Console.WriteLine("\nRun the following commands to clean up:");
        Console.WriteLine($"  systemctl --user stop {ServiceName}");
        Console.WriteLine($"  systemctl --user disable {ServiceName}");
        Console.WriteLine($"  systemctl --user daemon-reload");

        return Task.CompletedTask;
    }

    private static async Task<string?> FindOnPath(string name)
    {
        var psi = new ProcessStartInfo("which", name)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return null;

        var output = (await proc.StandardOutput.ReadToEndAsync()).Trim();
        await proc.WaitForExitAsync();

        return proc.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
    }
}
