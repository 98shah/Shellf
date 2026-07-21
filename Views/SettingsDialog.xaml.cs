using System.Windows;
using Shellf.Models;

namespace Shellf.Views;

public partial class SettingsDialog : Window
{
    private SettingsDialog(IReadOnlyList<ShellDefinition> shells, ShellDefinition current)
    {
        InitializeComponent();
        ShellList.ItemsSource = shells;
        ShellList.SelectedItem = current;
        MouseLeftButtonDown += (_, _) => DragMove();
    }

    public static ShellDefinition? Show(Window owner, IReadOnlyList<ShellDefinition> shells, ShellDefinition current)
    {
        var dialog = new SettingsDialog(shells, current) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.ShellList.SelectedItem as ShellDefinition : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
