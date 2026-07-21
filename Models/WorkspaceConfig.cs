namespace Shellf.Models;

/// <summary>
/// Root of the JSON document persisted to %APPDATA%\Shellf\workspace_config.json.
/// Plain DTOs on purpose — view models map to/from these.
/// </summary>
public sealed class WorkspaceConfig
{
    public string DefaultShellPath { get; set; } = string.Empty;

    /// <summary>Sidebar tree, in order: loose tabs and groups can be mixed freely.</summary>
    public List<WorkspaceItemConfig> Items { get; set; } = [];

    /// <summary>Pre-tree config format (flat groups only); imported once on load.</summary>
    public List<GroupConfig>? Groups { get; set; }
}

public sealed class WorkspaceItemConfig
{
    public string Type { get; set; } = "tab"; // "tab" | "group"

    // Type == "tab"
    public TabConfig? Tab { get; set; }

    // Type == "group"
    public string Name { get; set; } = string.Empty;
    public bool IsExpanded { get; set; } = true;
    public string Color { get; set; } = string.Empty; // row tint hex, empty = default
    public List<TabConfig> Tabs { get; set; } = [];
}

public sealed class GroupConfig
{
    public string Name { get; set; } = string.Empty;
    public List<TabConfig> Tabs { get; set; } = [];
}

public sealed class TabConfig
{
    public string Title { get; set; } = string.Empty;
    public string ShellPath { get; set; } = string.Empty;

    // The shell's live directory at save time (from OSC 9;9 tracking), not the launch dir.
    // Arguments are intentionally NOT persisted: they are derived from the shell catalog
    // at spawn time, so integration hooks can evolve without stale configs pinning them.
    public string WorkingDirectory { get; set; } = string.Empty;
}
