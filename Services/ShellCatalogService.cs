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
    //              xterm.js draws a separator line at each block.
    // Bottom-anchored input, self-healing: every prompt measures how far the cursor
    // sits above the window's last row and pads exactly that gap — zero in normal
    // flow, a full screen on the first prompt or after clear/Ctrl+L, and whatever a
    // full-screen app (vim, Claude Code) left behind on exit. The input line
    // therefore always renders on the bottom row, and output scrolls up above it.
    // Sessions spawn only after the terminal view reports the real grid size (see
    // TerminalHostService), so the measured window height is reliable from the
    // first prompt. The `n spacer row (used by the block separator) is skipped
    // whenever padding occurs. 38;2;0;255;0 renders the prompt in #00FF00.
    // Works for both Windows PowerShell and PowerShell 7 (pwsh).
    private const string PowerShellPromptHook = """
        $global:__ShellfPrompt = $function:prompt
        function global:prompt {
            $p = $ExecutionContext.SessionState.Path.CurrentLocation.ProviderPath
            $e = [char]27
            $gap = [Console]::WindowTop + [Console]::WindowHeight - 1 - [Console]::CursorTop
            $pad = ""
            $spacer = "`n"
            if ($gap -gt 0) {
                $pad = "`n" * $gap
                $spacer = ""
            }
            "$pad$e]133;D$e\$spacer$e]133;A$e\$e[38;2;0;255;0m" + (& $global:__ShellfPrompt) + "$e[0m$e]9;9;`"$p`"$e\$e]133;B$e\"
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
        // Git Bash via PROMPT_COMMAND (see TerminalHostService). The /K loop scrolls
        // blank lines so cmd's first prompt is bottom-anchored like the other shells
        // (200 overshoots any pane height; the surplus lands in scrollback).
        shells.Add(new ShellDefinition("Command Prompt", Path.Combine(system32, "cmd.exe"), "/K \"for /L %i in (1,1,200) do @echo.\""));

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
