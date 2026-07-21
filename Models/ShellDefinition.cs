namespace Shellf.Models;

/// <summary>An installed shell the user can open a terminal tab for.</summary>
public sealed record ShellDefinition(string DisplayName, string ExecutablePath, string Arguments);
