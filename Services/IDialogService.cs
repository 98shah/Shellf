using Shellf.Models;

namespace Shellf.Services;

/// <summary>View-layer dialogs, abstracted so view models stay window-free.</summary>
public interface IDialogService
{
    /// <summary>Settings dialog. Returns the chosen default shell, or null when cancelled.</summary>
    ShellDefinition? PickDefaultShell(IReadOnlyList<ShellDefinition> shells, ShellDefinition current);
}
