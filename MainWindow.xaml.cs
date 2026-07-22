using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Shellf.ViewModels;

namespace Shellf;

public partial class MainWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        StateChanged += (_, _) => UpdateChromeForState();
        Loaded += (_, _) => UpdateChromeForState();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.HasUnsavedChanges())
        {
            var choice = Views.ConfirmDialog.Show(
                this,
                "Unsaved workspace changes",
                "The workspace has changes that aren't saved. Save before closing?");

            if (choice is null)
                e.Cancel = true;            // Cancel: stay open
            else if (choice == true)
                viewModel.SaveWorkspaceCommand.Execute(null);
            // false: close without saving
        }

        if (!e.Cancel && DataContext is MainWindowViewModel closingViewModel)
            closingViewModel.SaveNotesQuiet();

        base.OnClosing(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // The caption is custom-drawn, but the dark hint keeps residual native chrome
        // (frame sliver, transition frames) from flashing light.
        var handle = new WindowInteropHelper(this).Handle;
        var enable = 1;
        _ = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enable, sizeof(int));
    }

    /// <summary>With WindowChrome, a maximized window bleeds past the screen edges by
    /// the resize border — compensate so content stays fully visible.</summary>
    private void UpdateChromeForState()
    {
        RootLayout.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxRestoreButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaxRestoreButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    private void OnMinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaxRestoreWindow(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void OnCloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.SelectedItem = e.NewValue;
    }

    private void OnTreePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        // Clicks inside an inline rename box are caret/selection work — activating
        // or drag-arming the row here would fight the TextBox.
        if (HasAncestor<TextBox>(e.OriginalSource))
            return;

        var item = GetRowItem(e.OriginalSource);

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && item is not null)
        {
            // Shift+Click: range from the anchor. Swallowed so the active tab
            // doesn't jump while range-selecting.
            _dragTab = null;
            viewModel.MarkRangeTo(item);
            WorkspaceTree.Focus();
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && item is not null)
        {
            // Ctrl+Click: toggle the row into the multi-selection.
            _dragTab = null;
            viewModel.ToggleMark(item);
            WorkspaceTree.Focus();
            e.Handled = true;
        }
        else
        {
            viewModel.ClearMarks(); // plain click resets the multi-selection
            viewModel.SetAnchor(item);
            if (item is TerminalTabViewModel tab)
            {
                viewModel.ActivateTab(tab); // first click selects, focus-independent
                // Candidate for drag & drop, unless the press is on a row button (✕, +).
                _dragTab = IsOnButton(e.OriginalSource) ? null : tab;
                _dragStart = e.GetPosition(WorkspaceTree);
            }
            else
            {
                _dragTab = null;
            }
        }
    }

    private TerminalTabViewModel? _dragTab;
    private Point _dragStart;

    private void OnTreePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTab is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var position = e.GetPosition(WorkspaceTree);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var tab = _dragTab;
        _dragTab = null;
        DragDrop.DoDragDrop(WorkspaceTree, new DataObject("ShellfTab", tab), DragDropEffects.Move);
    }

    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("ShellfTab") ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTreeDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("ShellfTab") is not TerminalTabViewModel tab ||
            DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.MoveTab(tab, GetRowItem(e.OriginalSource));
        e.Handled = true;
    }

    private static bool IsOnButton(object originalSource) => HasAncestor<ButtonBase>(originalSource);

    private static bool HasAncestor<T>(object originalSource) where T : DependencyObject
    {
        var current = originalSource as DependencyObject;
        while (current is not null and not TreeViewItem)
        {
            if (current is T)
                return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private static object? GetRowItem(object originalSource)
    {
        var current = originalSource as DependencyObject;
        while (current is not null and not TreeViewItem)
            current = VisualTreeHelper.GetParent(current);
        return (current as TreeViewItem)?.DataContext;
    }

    private ScrollViewer? _treeScroller;

    private void OnTreeMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Damped pixel scrolling: the default (3 tree rows per notch) feels jumpy
        // with two-line tab rows.
        _treeScroller ??= FindScrollViewer(WorkspaceTree);
        if (_treeScroller is null)
            return;

        _treeScroller.ScrollToVerticalOffset(_treeScroller.VerticalOffset - e.Delta * 0.25);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scroller)
                return scroller;
            if (FindScrollViewer(child) is { } nested)
                return nested;
        }
        return null;
    }

    private void OnGroupMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || DataContext is not MainWindowViewModel viewModel)
            return;
        if (menu.PlacementTarget is not FrameworkElement { DataContext: TabGroupViewModel group })
            return;
        if (menu.Items.Count == 0 || menu.Items[0] is not MenuItem addTab)
            return;

        // Shell-picker submenu, rebuilt per open from the detected catalog.
        var shellIcons = new Views.ShellIconConverter();
        addTab.Items.Clear();
        foreach (var shell in viewModel.AvailableShells)
        {
            addTab.Items.Add(new MenuItem
            {
                Header = MenuHeader(
                    shell.DisplayName,
                    shellIcons.Convert(shell.ExecutablePath, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture)),
                Command = viewModel.AddShellTabToGroupCommand,
                CommandParameter = new AddTabToGroupRequest(group, shell),
            });
        }
    }

    private static object MenuHeader(string text, object? iconSource)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Image
        {
            Source = iconSource as System.Windows.Media.ImageSource,
            Width = 13,
            Height = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }

    private GridLength _savedSidebarWidth = new(260);
    private bool _sidebarCollapsed;

    private void OnToggleSidebar(object sender, RoutedEventArgs e)
    {
        if (_sidebarCollapsed)
        {
            _sidebarCollapsed = false;
            AnimateColumn(SidebarColumn, 0, _savedSidebarWidth.Value, () =>
            {
                SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
                SidebarColumn.Width = _savedSidebarWidth;
                SidebarColumn.MinWidth = 180;
                PaneSplitter.IsEnabled = true;
            });
        }
        else
        {
            _sidebarCollapsed = true;
            if (SidebarColumn.ActualWidth > 0)
                _savedSidebarWidth = new GridLength(SidebarColumn.ActualWidth);
            PaneSplitter.IsEnabled = false;
            SidebarColumn.MinWidth = 0;
            AnimateColumn(SidebarColumn, SidebarColumn.ActualWidth, 0, () =>
            {
                SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
                SidebarColumn.Width = new GridLength(0); // fully hidden; toggle lives in the title bar
            });
        }
    }

    private DateTime _notesClosedAt = DateTime.MinValue;

    private void OnToggleNotes(object sender, RoutedEventArgs e)
    {
        // Clicking the button while the popup is open closes it via its own
        // outside-click handling just before this fires — don't instantly reopen.
        if ((DateTime.Now - _notesClosedAt).TotalMilliseconds < 250)
            return;

        NotesPopup.IsOpen = true;
        NotesBox.Focus();
    }

    private void OnNotesPopupClosed(object? sender, EventArgs e) => _notesClosedAt = DateTime.Now;

    private static void AnimateColumn(ColumnDefinition column, double from, double to, Action onCompleted)
    {
        var animation = new Views.GridLengthAnimation
        {
            From = new GridLength(from),
            To = new GridLength(to),
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            Easing = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            },
        };
        animation.Completed += (_, _) => onCompleted();
        column.BeginAnimation(ColumnDefinition.WidthProperty, animation);
    }

    private void OnCopyNoteToInput(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;
        var text = NotesBox.SelectionLength > 0 ? NotesBox.SelectedText : NotesBox.Text;
        viewModel.SendToActiveInput(text);
        NotesPopup.IsOpen = false; // hand the stage back to the terminal
    }

    /// <summary>Focus can only land once the box is rendered — containers for
    /// brand-new tabs materialize a beat after the view model adds them.</summary>
    private void OnRenameBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox box)
            Dispatcher.InvokeAsync(() =>
            {
                box.Focus();
                box.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnRenameBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        if (e.Key == Key.Enter)
        {
            CommitRename(element.DataContext);
            TerminalHost.FocusTerminal(); // named it — now type in it
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelRename(element.DataContext);
            TerminalHost.FocusTerminal();
            e.Handled = true;
        }
    }

    /// <summary>Clicking anywhere else commits, like Explorer's inline rename.</summary>
    private void OnRenameBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
            CommitRename(element.DataContext);
    }

    private static void CommitRename(object? item)
    {
        switch (item)
        {
            case TerminalTabViewModel tab:
                tab.CommitRename();
                break;
            case TabGroupViewModel group:
                group.CommitRename();
                break;
        }
    }

    private static void CancelRename(object? item)
    {
        switch (item)
        {
            case TerminalTabViewModel tab:
                tab.CancelRename();
                break;
            case TabGroupViewModel group:
                group.CancelRename();
                break;
        }
    }

    private void OnGroupHeaderClick(object sender, MouseButtonEventArgs e)
    {
        // Group headers are not selectable: a plain click just toggles the fold.
        // (Ctrl/Shift clicks are consumed earlier by the multi-select handler.)
        if (sender is FrameworkElement { DataContext: TabGroupViewModel group })
        {
            group.IsExpanded = !group.IsExpanded;
            e.Handled = true;
        }
    }

    private void OnAddMenuClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var button = (Button)sender;
        var menu = button.ContextMenu!;

        // Rebuilt each time from whatever shells were detected on this machine.
        var shellIcons = new Views.ShellIconConverter();
        menu.Items.Clear();
        foreach (var shell in viewModel.AvailableShells)
        {
            menu.Items.Add(new MenuItem
            {
                Header = MenuHeader(
                    $"Add {shell.DisplayName}",
                    shellIcons.Convert(shell.ExecutablePath, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture)),
                Command = viewModel.AddShellTabCommand,
                CommandParameter = shell,
            });
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = MenuHeader("Add Group", TryFindResource("Icon.Folder")),
            Command = viewModel.AddGroupCommand,
        });

        menu.DataContext = DataContext;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
