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

    /// <summary>Shows the inline rename box in the sidebar row.</summary>
    [ObservableProperty]
    private bool _isRenaming;

    /// <summary>Scratch text for the inline rename box; <see cref="Name"/> changes only on commit.</summary>
    [ObservableProperty]
    private string _editingName = string.Empty;

    /// <summary>Row tint (hex with alpha), chosen from the context menu.</summary>
    [ObservableProperty]
    private string? _colorHex = DefaultColorHex;

    public ObservableCollection<TerminalTabViewModel> Tabs { get; } = [];

    public TabGroupViewModel(string name) => _name = name;

    [RelayCommand]
    private void SetColor(string? colorHex) => ColorHex = colorHex;

    public void BeginRename()
    {
        EditingName = Name;
        IsRenaming = true;
    }

    /// <summary>Idempotent: Enter commits first, then the box's focus loss fires again.</summary>
    public void CommitRename()
    {
        if (!IsRenaming)
            return;
        IsRenaming = false;

        var name = EditingName.Trim();
        if (name.Length > 0)
            Name = name; // empty input keeps the old name, matching the old dialog
    }

    public void CancelRename() => IsRenaming = false;
}
