using System.Windows;
using Shellf.Models;
using Shellf.Services;

namespace Shellf.Views;

public sealed class DialogService : IDialogService
{
    public ShellDefinition? PickDefaultShell(IReadOnlyList<ShellDefinition> shells, ShellDefinition current) =>
        SettingsDialog.Show(Application.Current.MainWindow, shells, current);
}
