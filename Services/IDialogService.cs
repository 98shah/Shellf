using Shellf.Models;

namespace Shellf.Services;

/// <summary>View-layer dialogs, abstracted so view models stay window-free.</summary>
public interface IDialogService
{
    /// <summary>Modal text prompt. Returns the entered text, or null when cancelled.</summary>
    string? PromptText(string title, string initialValue);

    /// <summary>Settings dialog. Returns the chosen default shell, or null when cancelled.</summary>
    ShellDefinition? PickDefaultShell(IReadOnlyList<ShellDefinition> shells, ShellDefinition current);
}
