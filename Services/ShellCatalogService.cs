using System.IO;
using System.Text;
using Microsoft.Win32;
using Shellf.Models;

namespace Shellf.Services;

public sealed class ShellCatalogService : IShellCatalogService
{
    // Wraps the user's prompt (custom prompts survive) with invisible shell-integration
    // markers, both Windows Terminal conventions:
    //   OSC 9;9  — current directory; parsed by TerminalHostService so "Save Workspace"
    //              persists where each shell actually is, not where it started.
    //   OSC 133  — command block marks (D = done, A = prompt start, B = input starts);
    //              unused by the view today, reserved for the block UI planned for
    //              the next version.
    // 38;2;0;255;0 renders the visible prompt in neon #00FF00.
    // Works for both Windows PowerShell and PowerShell 7 (pwsh).
    private const string PowerShellPromptHook = """
        $global:__ShellfPrompt = $function:prompt
        function global:prompt {
            $p = $ExecutionContext.SessionState.Path.CurrentLocation.ProviderPath
            $e = [char]27
            "$e]133;D$e\$e]133;A$e\$e[38;2;0;255;0m" + (& $global:__ShellfPrompt) + "$e[0m$e]9;9;`"$p`"$e\$e]133;B$e\"
        }
        """;

    private static readonly string PowerShellArguments =
        "-NoLogo -NoExit -EncodedCommand " +
        Convert.ToBase64String(Encoding.Unicode.GetBytes(PowerShellPromptHook));

    public IReadOnlyList<ShellDefinition> GetInstalledShells()
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var shells = new List<ShellDefinition>
        {
            new("PowerShell", Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe"), PowerShellArguments),
        };

        if (FindOnPath("pwsh.exe") is { } pwsh)
            shells.Add(new ShellDefinition("PowerShell 7", pwsh, PowerShellArguments));

        // cmd gets its cwd/block markers via the PROMPT environment variable, and
        // Git Bash via PROMPT_COMMAND (see TerminalHostService).
        shells.Add(new ShellDefinition("Command Prompt", Path.Combine(system32, "cmd.exe"), string.Empty));

        if (FindGitBash() is { } gitBash)
            shells.Add(new ShellDefinition("Git Bash", gitBash, "--login -i"));

        // WSL is left as-is: its prompt is owned by the Linux-side shell config.
        shells.Add(new ShellDefinition("WSL", Path.Combine(system32, "wsl.exe"), string.Empty));

        return shells.Where(shell => File.Exists(shell.ExecutablePath)).ToList();
    }

    private static string? FindOnPath(string executableName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, executableName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry; skip it.
            }
        }
        return null;
    }

    private static string? FindGitBash()
    {
        // The installer-registered location first, then the common defaults.
        var installPath =
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\GitForWindows", "InstallPath", null) as string
            ?? Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\GitForWindows", "InstallPath", null) as string;

        string?[] candidates =
        [
            installPath is null ? null : Path.Combine(installPath, "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "bin", "bash.exe"),
        ];

        return candidates.FirstOrDefault(candidate => candidate is not null && File.Exists(candidate));
    }
}
