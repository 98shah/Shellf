using System.Windows;

namespace Shellf.Views;

/// <summary>Minimal dark modal for entering a single line of text (rename, etc.).</summary>
public partial class PromptDialog : Window
{
    private PromptDialog(string title, string initialValue)
    {
        InitializeComponent();
        TitleText.Text = title;
        Input.Text = initialValue;
        MouseLeftButtonDown += (_, _) => DragMove();
        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    public static string? Show(Window owner, string title, string initialValue)
    {
        var dialog = new PromptDialog(title, initialValue) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Input.Text.Trim() : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
