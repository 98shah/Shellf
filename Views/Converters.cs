using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Shellf.Views;

/// <summary>The group's stored tint hex at FULL opacity — the accent used for the
/// group's name and icon (GitHub-label style: faint band, vivid text).</summary>
public sealed class ColorHexToAccentBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && hex.Length > 0)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
                brush.Freeze();
                return brush;
            }
            catch (Exception)
            {
                // ConvertFromString throws more than FormatException on some
                // malformed config values; any parse failure means the fallback.
            }
        }
        return Application.Current.TryFindResource("Brush.Text") ?? Brushes.White;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Picks the tab icon for a shell path; unmapped shells get the plain
/// terminal chevron as the default.</summary>
public sealed class ShellIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value is string path ? Path.GetFileName(path).ToLowerInvariant() : null) switch
        {
            "cmd.exe" => "Icon.Cmd",
            "bash.exe" => "Icon.GitBash",
            "wsl.exe" => "Icon.Wsl",
            _ => "Icon.Terminal",
        };
        return Application.Current.TryFindResource(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Hex string (e.g. "#3A4F6B") to a frozen brush; null/empty/invalid = transparent.</summary>
public sealed class ColorHexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && hex.Length > 0)
        {
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                return brush;
            }
            catch (Exception)
            {
                // Fall through to transparent on a malformed value in the config
                // file, whatever exception type the parser chose to throw.
            }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
