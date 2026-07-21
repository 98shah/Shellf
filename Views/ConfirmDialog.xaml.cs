using System.Windows;

namespace Shellf.Views;

/// <summary>Dark three-way confirm: true = Save, false = Don't Save, null = Cancel.</summary>
public partial class ConfirmDialog : Window
{
    private bool? _choice;

    private ConfirmDialog(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        MouseLeftButtonDown += (_, _) => DragMove();
    }

    public static bool? Show(Window owner, string title, string message)
    {
        var dialog = new ConfirmDialog(title, message) { Owner = owner };
        dialog.ShowDialog();
        return dialog._choice;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _choice = true;
        DialogResult = true;
    }

    private void OnDontSave(object sender, RoutedEventArgs e)
    {
        _choice = false;
        DialogResult = true;
    }
}
