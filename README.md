# Shellf

A fast, dark, keyboard-friendly terminal workspace manager for Windows.
Organize all your shells — PowerShell, Command Prompt, Git Bash, WSL — into one
window, group them by project, and pick up tomorrow exactly where you left off today.

## Features

- **Real terminals.** Every tab is a fully interactive shell: tab completion,
  command history, colors, `clear`, and full-screen apps all work exactly as they
  do in a native terminal.
- **Workspace tree.** Keep standalone tabs, or organize them into collapsible
  groups with color tags. Rename anything, reorder with drag & drop, multi-select
  with Ctrl/Shift+Click, and manage everything from the right-click menu.
- **Knows where you are.** Each tab shows the shell's current directory live, and
  restoring a workspace reopens every terminal in the directory it was in — not
  where it started.
- **Save and restore.** One click (or Ctrl+S) saves your entire layout — tabs,
  groups, colors, directories. Reopen Shellf and the whole workspace comes back.
  If you close with unsaved changes, Shellf asks first.
- **Your shells, detected.** Installed shells appear automatically — PowerShell,
  Command Prompt, Git Bash, WSL — and you choose which one the **+** button opens
  by default.
- **Built for dark mode.** A single consistent dark theme from the title bar to
  the terminal, with a collapsible sidebar and a clean, icon-driven UI.

## Getting started

Requirements: Windows 11 (or Windows 10 with the WebView2 Runtime) and the
[.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source.

```powershell
git clone <repository-url>
cd Shellf
dotnet run
```

## Quick guide

| Action | How |
|---|---|
| New terminal (default shell) | **+ Add Tab**, or the **+** on a group row |
| New terminal (specific shell) / new group | the **▾** menu |
| Switch terminal | click a tab in the sidebar |
| Collapse / expand a group | click its header |
| Rename, delete, recolor, group | right-click a tab or group |
| Multi-select | Ctrl+Click or Shift+Click, then right-click or press Del |
| Move tabs between groups | drag & drop |
| Save workspace | **Save Workspace** or Ctrl+S |
| Default shell | **⚙ Settings** |

Your workspace is stored per-user under `%APPDATA%\Shellf`.
