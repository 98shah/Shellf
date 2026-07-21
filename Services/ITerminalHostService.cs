namespace Shellf.Services;

/// <summary>A chunk of raw VT output. Offset is the byte position of the chunk within
/// the session's whole output stream, used by consumers to avoid double-writes after
/// a replay.</summary>
public sealed record TerminalOutputEventArgs(string SessionId, byte[] Data, long Offset);

/// <summary>Raised when a shell reports a NEW current directory via its prompt marker.</summary>
public sealed record TerminalCwdEventArgs(string SessionId, string Path);

/// <summary>
/// Owns every live ConPTY session, keyed by session id. View models start/stop sessions;
/// the terminal view (xterm.js host) attaches to the events and streams. A bounded replay
/// buffer per session lets the view render output that arrived before it was ready.
/// </summary>
public interface ITerminalHostService
{
    IReadOnlyList<string> ActiveSessionIds { get; }

    void StartSession(string sessionId, string shellPath, string arguments, string workingDirectory);

    /// <summary>Raw input from the terminal view (keystrokes, escape sequences).</summary>
    void SendInput(string sessionId, string data);

    void Resize(string sessionId, int cols, int rows);

    void CloseSession(string sessionId);

    /// <summary>Buffered output so far and the stream offset it ends at.</summary>
    (byte[] Data, long EndOffset) GetReplay(string sessionId);

    /// <summary>
    /// The shell's current directory as last reported via its OSC 9;9 prompt marker,
    /// or null when the shell has not reported one (e.g. WSL).
    /// </summary>
    string? GetCurrentDirectory(string sessionId);

    /// <summary>Raised on background threads.</summary>
    event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    /// <summary>Raised on background threads when a shell's directory changes.</summary>
    event EventHandler<TerminalCwdEventArgs>? CurrentDirectoryChanged;

    /// <summary>Raised synchronously from <see cref="StartSession"/>.</summary>
    event EventHandler<string>? SessionStarted;

    event EventHandler<string>? SessionClosed;

    void DisposeAll();
}
