using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shellf.ViewModels;

/// <summary>A collapsible folder of terminal tabs in the sidebar tree.</summary>
public sealed partial class TabGroupViewModel : ObservableObject
{
    /// <summary>The neutral grey tint every group starts with (also the "Default"
    /// entry in the colour menu — keep the XAML swatch in sync).</summary>
    public const string DefaultColorHex = "#269CA3AF";

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Part of the Ctrl+Click multi-selection (for bulk delete).</summary>
    [ObservableProperty]
    private bool _isMarked;

    /// <summary>Row tint (hex with alpha), chosen from the context menu.</summary>
    [ObservableProperty]
    private string? _colorHex = DefaultColorHex;

    public ObservableCollection<TerminalTabViewModel> Tabs { get; } = [];

    public TabGroupViewModel(string name) => _name = name;

    [RelayCommand]
    private void SetColor(string? colorHex) => ColorHex = colorHex;
}
