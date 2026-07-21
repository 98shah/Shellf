# Shellf

A lightweight, dark-themed terminal workspace manager for Windows.
WPF on **.NET 10**, MVVM with **CommunityToolkit.Mvvm**, IoC with **Microsoft.Extensions.DependencyInjection**,
real terminals via **ConPTY + xterm.js** in a single shared WebView2.

## Features

- **Real terminals.** Each tab runs its shell attached to a Windows pseudo console
  (ConPTY), so the shell behaves exactly like in Windows Terminal: PSReadLine,
  tab completion, arrow-key history, syntax colors, `clear`, and full-screen apps.
- **Workspace tree in the sidebar.** Tabs live on the left: loose independent tabs
  and collapsible groups (folders) can be mixed freely. Right-click any tab or group
  for Rename / Delete (groups also get Add Tab).
- **Three-button toolbar.** `+` opens a tab with the default shell; the `▾` menu has
  Add PowerShell / Add Cmd / Add Group (a new group opens with one default-shell tab);
  `⚙` opens Settings, where the default shell is chosen (PowerShell out of the box).
- **Save Workspace** (bottom of the sidebar, or <kbd>Ctrl+S</kbd> outside the terminal)
  persists the tree, tab titles, shell paths and **each shell's live current directory**
  to `%APPDATA%\Shellf\workspace_config.json`.
- **Live directory tracking** via the OSC 9;9 shell-integration marker (the Windows
  Terminal convention): PowerShell through an injected prompt hook that wraps (not
  replaces) any custom prompt, cmd through the `PROMPT` environment variable.
  Terminal output/history is deliberately not persisted.
- **Automatic restore** — on launch the saved tree is rebuilt and every shell starts
  in its saved directory. No splash screen; the window opens maximized, immediately.
- **Sharp dark UI** — black/grey palette, square corners throughout, Inter as the
  UI font (terminal text stays Cascadia Mono — a monospace font is required there).

## Architecture: one WebView2, many terminals

The app chrome (sidebar tree, dialogs, persistence) is pure native WPF.
**One** WebView2 instance hosts `Assets/terminal.html`, and every terminal is an
xterm.js instance inside that single page — switching tabs toggles DOM panes, so the
overhead is a single browser renderer, not one per tab.

```
keystrokes:  xterm.js ──postMessage──▶ TerminalHostView ──▶ ITerminalHostService ──▶ ConPTY stdin
output:      ConPTY stdout ──▶ TerminalHostService (replay buffer) ──event──▶ TerminalHostView ──▶ xterm.write
```

`TerminalHostService` keeps a bounded replay buffer per session (with stream offsets),
so output produced before the WebView finishes loading — e.g. the prompts of restored
tabs — is replayed exactly once, with no gaps or duplicates.

## Build & run

Requires the .NET 10 SDK and the WebView2 Runtime (preinstalled on Windows 11).

```powershell
dotnet run --project .\Shellf.csproj
```

## NuGet packages

```powershell
dotnet add package CommunityToolkit.Mvvm --version 8.4.0
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.0
dotnet add package Microsoft.Web.WebView2 --version 1.0.2903.40
```

`Assets/` bundles xterm.js 5.3.0 + fit addon, served to WebView2 through a
virtual-host mapping — nothing is loaded from the network at runtime.

## Project layout

```
Shellf/
├── App.xaml / App.xaml.cs            Entry point: DI container, shows MainWindow immediately
├── MainWindow.xaml(.cs)              Sidebar (action row + workspace tree + save) and terminal pane
├── Assets/
│   ├── terminal.html                 Hosts all xterm.js instances; message protocol with C#
│   └── xterm.js / xterm.css / xterm-addon-fit.js
├── Models/
│   ├── WorkspaceConfig.cs            JSON DTOs (tree items, tabs, default shell)
│   └── ShellDefinition.cs            An installed shell (name, path, args)
├── ViewModels/
│   ├── MainWindowViewModel.cs        Tree, commands, settings, save/load orchestration
│   ├── TabGroupViewModel.cs          A collapsible folder of tabs
│   └── TerminalTabViewModel.cs       One tab: metadata + session id (no terminal I/O)
├── Services/
│   ├── IWorkspaceStorageService.cs / WorkspaceStorageService.cs   JSON persistence
│   ├── IShellCatalogService.cs / ShellCatalogService.cs           Shell discovery + prompt hooks
│   ├── IDialogService.cs                                          View-layer dialog abstraction
│   ├── ITerminalHostService.cs / TerminalHostService.cs           Session registry + replay buffers
│   └── ConPty/
│       ├── ConPtyNative.cs           Win32 P/Invoke surface for the pseudo console API
│       ├── ConPtySession.cs          One shell attached to a ConPTY (spawn, pump, resize, kill)
│       └── KillOnCloseJob.cs         OS-level guarantee that shells die with the app
├── Views/
│   ├── TerminalHostView.xaml(.cs)    The single WebView2; bridges service events ⇄ xterm.js
│   ├── PromptDialog.xaml(.cs)        Dark rename prompt
│   ├── SettingsDialog.xaml(.cs)      Default-shell picker
│   └── DialogService.cs              IDialogService implementation
└── Themes/
    └── DarkTheme.xaml                Palette + styles (buttons, tree, menus, scrollbars)
```

## Notes

- WSL's Linux-side directory is not tracked (its paths aren't Windows paths);
  WSL tabs restore in the directory they were launched from.
- <kbd>Ctrl+S</kbd> saves only while WPF has keyboard focus; inside the terminal,
  keystrokes belong to the shell (as they should). The sidebar button always works.
- Closing a tab, deleting a group, or exiting the app kills the shell's whole
  process tree — no orphaned shells (job-object enforced, crash-proof).
