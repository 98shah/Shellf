using CommunityToolkit.Mvvm.ComponentModel;

namespace Shellf.ViewModels;

/// <summary>
/// One terminal tab. Pure metadata: the live session lives in ITerminalHostService
/// (keyed by <see cref="SessionId"/>) and renders in the shared terminal view.
/// Can sit at the root of the sidebar tree or inside a <see cref="TabGroupViewModel"/>.
/// </summary>
public sealed partial class TerminalTabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title;

    /// <summary>Live current directory of the shell (from OSC 9;9 prompt markers),
    /// shown as subtext under the tab name.</summary>
    [ObservableProperty]
    private string _currentDirectory;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Part of the Ctrl+Click multi-selection (for bulk delete).</summary>
    [ObservableProperty]
    private bool _isMarked;

    public string SessionId { get; }
    public string ShellPath { get; }

    /// <summary>The directory the shell was launched in. The live directory is
    /// tracked by the terminal host and queried at save time.</summary>
    public string LaunchDirectory { get; }

    public TerminalTabViewModel(string sessionId, string title, string shellPath, string launchDirectory)
    {
        SessionId = sessionId;
        _title = title;
        ShellPath = shellPath;
        LaunchDirectory = launchDirectory;
        _currentDirectory = launchDirectory;
    }
}
