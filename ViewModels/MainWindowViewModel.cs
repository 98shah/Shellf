using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shellf.Models;
using Shellf.Services;

namespace Shellf.ViewModels;

/// <summary>Parameter for the group context menu's shell-picker submenu.</summary>
public sealed record AddTabToGroupRequest(TabGroupViewModel Group, ShellDefinition Shell);

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IWorkspaceStorageService _storage;
    private readonly ITerminalHostService _terminalHost;
    private readonly IDialogService _dialogs;

    /// <summary>Sidebar tree roots: TerminalTabViewModel (loose tab) or TabGroupViewModel.</summary>
    public ObservableCollection<object> Items { get; } = [];

    public IReadOnlyList<ShellDefinition> AvailableShells { get; }

    /// <summary>Whatever tree node is selected; set by the view.</summary>
    [ObservableProperty]
    private object? _selectedItem;

    /// <summary>The tab whose terminal is shown in the main pane.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyActivePathCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenActivePathCommand))]
    private TerminalTabViewModel? _selectedTab;

    [ObservableProperty]
    private ShellDefinition _defaultShell = null!;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Free-form scratch notes shown in the right panel.</summary>
    [ObservableProperty]
    private string _notesText = string.Empty;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public MainWindowViewModel(
        IWorkspaceStorageService storage,
        IShellCatalogService shellCatalog,
        ITerminalHostService terminalHost,
        IDialogService dialogs)
    {
        _storage = storage;
        _terminalHost = terminalHost;
        _dialogs = dialogs;
        AvailableShells = shellCatalog.GetInstalledShells();
        _notesText = storage.LoadNotes();

        // Cwd reports arrive on pty read threads; the tab rows show them live.
        terminalHost.CurrentDirectoryChanged += (_, e) => _dispatcher.InvokeAsync(() =>
        {
            var tab = AllTabs().FirstOrDefault(t => t.SessionId == e.SessionId);
            if (tab is not null)
                tab.CurrentDirectory = e.Path;
        });

        LoadWorkspace();
    }

    private IEnumerable<TerminalTabViewModel> AllTabs() =>
        Items.OfType<TerminalTabViewModel>()
            .Concat(Items.OfType<TabGroupViewModel>().SelectMany(g => g.Tabs));

    partial void OnSelectedItemChanged(object? value)
    {
        if (value is TerminalTabViewModel tab)
            SelectedTab = tab;
        else if (value is TabGroupViewModel group)
            group.IsSelected = false; // groups are never selection-highlighted
    }

    [RelayCommand]
    private void AddDefaultTab() => AddTab(DefaultShell, group: null);

    /// <summary>The ▾ menu lists every detected shell and routes here.</summary>
    [RelayCommand]
    private void AddShellTab(ShellDefinition shell) => AddTab(shell, group: null);

    [RelayCommand]
    private void AddGroup()
    {
        var group = new TabGroupViewModel($"Group {Items.OfType<TabGroupViewModel>().Count() + 1}");
        Items.Add(group);
        AddTab(DefaultShell, group); // a new group opens with one default-shell tab
    }

    /// <summary>The little + on a group row: default shell into that group.</summary>
    [RelayCommand]
    private void AddTabToGroup(TabGroupViewModel group) => AddTab(DefaultShell, group);

    /// <summary>The group context menu's Add Tab submenu: chosen shell into that group.</summary>
    [RelayCommand]
    private void AddShellTabToGroup(AddTabToGroupRequest request) => AddTab(request.Shell, request.Group);

    /// <summary>Context-menu "Create Group": wraps the multi-selection (or the clicked
    /// tab) in a new group, inserted where the first tab was. Sessions are untouched.</summary>
    [RelayCommand]
    private void GroupTabs(object item)
    {
        var markedTabs = MarkedItems().OfType<TerminalTabViewModel>().ToList();
        var tabs = item is TerminalTabViewModel clicked
            ? (markedTabs.Count > 1 && markedTabs.Contains(clicked) ? markedTabs : [clicked])
            : markedTabs;
        if (tabs.Count == 0)
            return;

        var anchorIndex = RootIndexOf(tabs[0]);

        var group = new TabGroupViewModel($"Group {Items.OfType<TabGroupViewModel>().Count() + 1}");
        var previousOwners = new HashSet<TabGroupViewModel>();
        foreach (var tab in tabs)
        {
            if (Detach(tab) is { } owner)
                previousOwners.Add(owner);
            group.Tabs.Add(tab);
        }

        Items.Insert(Math.Min(anchorIndex, Items.Count), group);
        foreach (var owner in previousOwners)
            PruneIfEmpty(owner);
        ClearMarks();
    }

    /// <summary>Drag &amp; drop: onto a group = into it; onto a tab = after that tab
    /// (in whatever container it lives); onto empty space = loose at the end.</summary>
    public void MoveTab(TerminalTabViewModel tab, object? dropTarget)
    {
        if (ReferenceEquals(tab, dropTarget))
            return;

        // Prune only after the tab has landed, so dropping a group's sole tab back
        // onto that same group doesn't delete it mid-move.
        var previousOwner = Detach(tab);

        switch (dropTarget)
        {
            case TabGroupViewModel group:
                group.IsExpanded = true;
                group.Tabs.Add(tab);
                break;

            case TerminalTabViewModel other:
            {
                var owner = Items.OfType<TabGroupViewModel>().FirstOrDefault(g => g.Tabs.Contains(other));
                if (owner is null)
                {
                    var index = Items.IndexOf(other);
                    Items.Insert(index < 0 ? Items.Count : index + 1, tab);
                }
                else
                {
                    owner.Tabs.Insert(owner.Tabs.IndexOf(other) + 1, tab);
                }
                break;
            }

            default:
                Items.Add(tab);
                break;
        }

        PruneIfEmpty(previousOwner);
    }

    /// <summary>Removes the tab from wherever it lives; returns its former group (if any)
    /// so the caller can prune it once the operation is complete.</summary>
    private TabGroupViewModel? Detach(TerminalTabViewModel tab)
    {
        if (Items.Remove(tab))
            return null;

        var owner = Items.OfType<TabGroupViewModel>().FirstOrDefault(g => g.Tabs.Contains(tab));
        owner?.Tabs.Remove(tab);
        return owner;
    }

    /// <summary>A group that lost its last tab is removed with it.</summary>
    private void PruneIfEmpty(TabGroupViewModel? group)
    {
        if (group is not null && group.Tabs.Count == 0)
            Items.Remove(group);
    }

    private int RootIndexOf(TerminalTabViewModel tab)
    {
        var index = Items.IndexOf(tab);
        if (index >= 0)
            return index;
        var owner = Items.OfType<TabGroupViewModel>().FirstOrDefault(g => g.Tabs.Contains(tab));
        return owner is null ? Items.Count : Items.IndexOf(owner) + 1;
    }

    // Renaming several things at once is meaningless; the menu item greys out
    // when the row is part of a multi-selection.
    private bool CanRenameItem(object item)
    {
        var marked = MarkedItems();
        return !(marked.Count > 1 && marked.Contains(item));
    }

    [RelayCommand(CanExecute = nameof(CanRenameItem))]
    private void RenameItem(object item)
    {
        // In place, on the row itself — never a dialog.
        switch (item)
        {
            case TerminalTabViewModel tab:
                tab.BeginRename();
                break;
            case TabGroupViewModel group:
                group.BeginRename();
                break;
        }
    }

    /// <summary>Context-menu delete. When the clicked row is part of the Ctrl+Click
    /// multi-selection the whole selection is deleted; otherwise just that row
    /// (Explorer semantics).</summary>
    [RelayCommand]
    private void DeleteItem(object item)
    {
        var marked = MarkedItems();
        DeleteMany(marked.Count > 1 && marked.Contains(item) ? marked : [item]);
    }

    /// <summary>Del key: the multi-selection, or the focused row when nothing is marked.</summary>
    [RelayCommand]
    private void DeleteSelected()
    {
        var marked = MarkedItems();
        if (marked.Count > 0)
            DeleteMany(marked);
        else if (SelectedItem is { } item)
            DeleteMany([item]);
    }

    private object? _rangeAnchor;

    public void SetAnchor(object? item) => _rangeAnchor = item;

    public void ToggleMark(object item)
    {
        // The focused row implicitly joins the multi-selection when it starts, so
        // "click item1, Ctrl+Click item2" selects both — not just item2.
        if (MarkedItems().Count == 0 && SelectedItem is { } current && !ReferenceEquals(current, item))
            SetMarked(current, true);

        SetMarked(item, !IsMarkedItem(item));
        _rangeAnchor = item;
    }

    /// <summary>Shift+Click: mark everything between the anchor (last plain click or
    /// Ctrl+Click) and the given row, in visible order.</summary>
    public void MarkRangeTo(object item)
    {
        var order = VisibleItems();
        var anchor = _rangeAnchor ?? SelectedItem;
        var from = anchor is null ? -1 : order.IndexOf(anchor);
        var to = order.IndexOf(item);
        if (to < 0)
            return;
        if (from < 0)
            from = to;

        ClearMarks();
        var (start, end) = from <= to ? (from, to) : (to, from);
        for (var i = start; i <= end; i++)
            SetMarked(order[i], true);
    }

    public void ClearMarks()
    {
        foreach (var item in VisibleItems(includeCollapsed: true))
            SetMarked(item, false);
    }

    /// <summary>Tree rows in display order; collapsed groups hide their children
    /// (ranges operate on what the user can see).</summary>
    private List<object> VisibleItems(bool includeCollapsed = false)
    {
        var order = new List<object>();
        foreach (var item in Items)
        {
            order.Add(item);
            if (item is TabGroupViewModel group && (group.IsExpanded || includeCollapsed))
                order.AddRange(group.Tabs);
        }
        return order;
    }

    private static void SetMarked(object item, bool value)
    {
        switch (item)
        {
            case TerminalTabViewModel tab:
                tab.IsMarked = value;
                break;
            case TabGroupViewModel group:
                group.IsMarked = value;
                break;
        }
    }

    private static bool IsMarkedItem(object item) => item switch
    {
        TerminalTabViewModel tab => tab.IsMarked,
        TabGroupViewModel group => group.IsMarked,
        _ => false,
    };

    private List<object> MarkedItems()
    {
        var marked = new List<object>();
        foreach (var item in Items)
        {
            switch (item)
            {
                case TerminalTabViewModel tab when tab.IsMarked:
                    marked.Add(tab);
                    break;
                case TabGroupViewModel group:
                    if (group.IsMarked)
                        marked.Add(group);
                    marked.AddRange(group.Tabs.Where(t => t.IsMarked));
                    break;
            }
        }
        return marked;
    }

    private void DeleteMany(IReadOnlyCollection<object> targets)
    {
        var groups = targets.OfType<TabGroupViewModel>().ToList();
        foreach (var group in groups)
        {
            foreach (var tab in group.Tabs)
            {
                _terminalHost.CloseSession(tab.SessionId);
                if (ReferenceEquals(SelectedTab, tab))
                    SelectedTab = null;
            }
            Items.Remove(group);
        }

        foreach (var tab in targets.OfType<TerminalTabViewModel>())
        {
            if (groups.Any(g => g.Tabs.Contains(tab)))
                continue; // already gone with its group
            CloseTab(tab);
        }

        ClearMarks();
        _rangeAnchor = null;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var picked = _dialogs.PickDefaultShell(AvailableShells, DefaultShell);
        if (picked is null || picked == DefaultShell)
            return;

        DefaultShell = picked;

        // Persist only the setting; the workspace layout stays explicit-save.
        var config = _storage.Load();
        config.DefaultShellPath = picked.ExecutablePath;
        _storage.Save(config);
        StatusText = $"Default shell set to {picked.DisplayName}.";
    }

    private bool HasActiveTab => SelectedTab is not null;

    [RelayCommand]
    private void SaveNotes()
    {
        _storage.SaveNotes(NotesText);
        StatusText = "Notes saved.";
    }

    /// <summary>Silent notes persistence on app close — notes are low-stakes scratch
    /// text and should never be lost, independent of the workspace save prompt.</summary>
    public void SaveNotesQuiet()
    {
        try
        {
            _storage.SaveNotes(NotesText);
        }
        catch (Exception)
        {
            // Best effort; closing must not be blocked by a notes write failure.
        }
    }

    /// <summary>"Copy to Input &amp; Run": places note text on the active terminal's
    /// input line and executes it (a trailing Enter is sent).</summary>
    public void SendToActiveInput(string text)
    {
        if (SelectedTab is null || string.IsNullOrWhiteSpace(text))
            return;
        _terminalHost.SendInput(SelectedTab.SessionId, text.TrimEnd('\r', '\n') + "\r");
    }

    [RelayCommand(CanExecute = nameof(HasActiveTab))]
    private void CopyActivePath()
    {
        if (SelectedTab is not { } tab)
            return;
        System.Windows.Clipboard.SetText(tab.CurrentDirectory);
        StatusText = "Path copied to clipboard.";
    }

    [RelayCommand(CanExecute = nameof(HasActiveTab))]
    private void OpenActivePath()
    {
        if (SelectedTab is not { } tab || !Directory.Exists(tab.CurrentDirectory))
            return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{tab.CurrentDirectory}\"")
        {
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    private void SaveWorkspace()
    {
        _storage.Save(BuildConfig());
        StatusText = $"Workspace saved at {DateTime.Now:HH:mm:ss}";
    }

    /// <summary>True when the live workspace differs from what is on disk — compared
    /// as serialized snapshots, so every kind of change counts (tree structure,
    /// renames, colours, fold state, live directories, default shell).</summary>
    public bool HasUnsavedChanges()
    {
        var current = System.Text.Json.JsonSerializer.Serialize(BuildConfig());
        var saved = System.Text.Json.JsonSerializer.Serialize(_storage.Load());
        return current != saved;
    }

    private WorkspaceConfig BuildConfig() => new()
    {
        DefaultShellPath = DefaultShell.ExecutablePath,
        Items = Items.Select(ToItemConfig).OfType<WorkspaceItemConfig>().ToList(),
    };

    private void LoadWorkspace()
    {
        var config = _storage.Load();

        DefaultShell = AvailableShells.FirstOrDefault(s =>
                string.Equals(s.ExecutablePath, config.DefaultShellPath, StringComparison.OrdinalIgnoreCase))
            ?? FindShell("powershell.exe")
            ?? AvailableShells[0];

        var items = config.Items;
        if (items.Count == 0 && config.Groups is { Count: > 0 })
        {
            // Import the pre-tree config format (flat groups only).
            items = config.Groups
                .Select(g => new WorkspaceItemConfig { Type = "group", Name = g.Name, Tabs = g.Tabs })
                .ToList();
        }

        foreach (var itemConfig in items)
        {
            if (itemConfig.Type == "group")
            {
                var group = new TabGroupViewModel(itemConfig.Name)
                {
                    IsExpanded = itemConfig.IsExpanded,
                    ColorHex = string.IsNullOrEmpty(itemConfig.Color)
                        ? TabGroupViewModel.DefaultColorHex
                        : itemConfig.Color,
                };
                foreach (var tabConfig in itemConfig.Tabs)
                {
                    if (RestoreTab(tabConfig) is { } tab)
                        group.Tabs.Add(tab);
                }
                Items.Add(group);
            }
            else if (itemConfig.Tab is { } tabConfig && RestoreTab(tabConfig) is { } tab)
            {
                Items.Add(tab);
            }
        }

        if (Items.Count == 0)
        {
            AddTab(DefaultShell, group: null); // fresh workspace: one default tab
        }
        else
        {
            var firstTab = Items.OfType<TerminalTabViewModel>().FirstOrDefault()
                ?? Items.OfType<TabGroupViewModel>().SelectMany(g => g.Tabs).FirstOrDefault();
            if (firstTab is not null)
                Select(firstTab);
        }

        if (string.IsNullOrEmpty(StatusText))
            StatusText = items.Count > 0 ? "Workspace restored." : "New workspace.";
    }

    private void AddTab(ShellDefinition? shell, TabGroupViewModel? group)
    {
        if (shell is null)
        {
            StatusText = "That shell is not installed on this machine.";
            return;
        }

        var sameShellCount = Items.OfType<TerminalTabViewModel>().Count(t => t.ShellPath == shell.ExecutablePath)
            + Items.OfType<TabGroupViewModel>().Sum(g => g.Tabs.Count(t => t.ShellPath == shell.ExecutablePath));

        var tab = StartTab(
            $"{shell.DisplayName} {sameShellCount + 1}",
            shell,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (tab is null)
            return;

        if (group is null)
        {
            Items.Add(tab);
        }
        else
        {
            group.IsExpanded = true;
            group.Tabs.Add(tab);
        }

        Select(tab);
    }

    private void CloseTab(TerminalTabViewModel tab)
    {
        var previousOwner = Detach(tab);
        _terminalHost.CloseSession(tab.SessionId);
        if (ReferenceEquals(SelectedTab, tab))
            SelectedTab = null;
        PruneIfEmpty(previousOwner);
    }

    private TerminalTabViewModel? RestoreTab(TabConfig tabConfig)
    {
        var shell = AvailableShells.FirstOrDefault(s =>
            string.Equals(s.ExecutablePath, tabConfig.ShellPath, StringComparison.OrdinalIgnoreCase));
        if (shell is null)
        {
            StatusText = $"Skipped '{tabConfig.Title}': shell not installed.";
            return null;
        }

        return StartTab(tabConfig.Title, shell, tabConfig.WorkingDirectory);
    }

    /// <summary>Starts a ConPTY session and returns its tab, or null (with a status
    /// message) when the shell cannot be started.</summary>
    private TerminalTabViewModel? StartTab(string title, ShellDefinition shell, string workingDirectory)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        try
        {
            _terminalHost.StartSession(sessionId, shell.ExecutablePath, shell.Arguments, workingDirectory);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not start '{shell.ExecutablePath}': {ex.Message}";
            return null;
        }

        return new TerminalTabViewModel(sessionId, title, shell.ExecutablePath, workingDirectory);
    }

    private void Select(TerminalTabViewModel tab)
    {
        tab.IsSelected = true; // drives the TreeView container
        SelectedTab = tab;
    }

    /// <summary>Called by the view on tab clicks: selects deterministically instead of
    /// relying on TreeView's focus-driven selection, which loses the first click when
    /// keyboard focus is inside the terminal (WebView2).</summary>
    public void ActivateTab(TerminalTabViewModel tab) => Select(tab);

    private ShellDefinition? FindShell(string executableName) =>
        AvailableShells.FirstOrDefault(s =>
            string.Equals(Path.GetFileName(s.ExecutablePath), executableName, StringComparison.OrdinalIgnoreCase));

    private WorkspaceItemConfig? ToItemConfig(object item) => item switch
    {
        TerminalTabViewModel tab => new WorkspaceItemConfig { Type = "tab", Tab = ToTabConfig(tab) },
        TabGroupViewModel group => new WorkspaceItemConfig
        {
            Type = "group",
            Name = group.Name,
            IsExpanded = group.IsExpanded,
            Color = group.ColorHex ?? string.Empty,
            Tabs = group.Tabs.Select(ToTabConfig).ToList(),
        },
        _ => null,
    };

    private TabConfig ToTabConfig(TerminalTabViewModel tab) => new()
    {
        Title = tab.Title,
        ShellPath = tab.ShellPath,
        // Where the shell IS right now (via its prompt marker), not where it started.
        WorkingDirectory = _terminalHost.GetCurrentDirectory(tab.SessionId) ?? tab.LaunchDirectory,
    };
}
