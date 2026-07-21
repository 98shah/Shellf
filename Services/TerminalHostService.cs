using System.IO;
using System.Text;
using Shellf.Services.ConPty;

namespace Shellf.Services;

public sealed class TerminalHostService : ITerminalHostService
{
    // Per-session replay cap; enough to repaint a screenful plus scrollback context.
    private const int ReplayCapacity = 256 * 1024;
    private const int DefaultCols = 120;
    private const int DefaultRows = 30;

    // ESC ] 9 ; 9 ;  — the shell-integration "current directory" marker.
    private static readonly byte[] OscCwdPrefix = "\x1b]9;9;"u8.ToArray();

    private sealed class HostedSession
    {
        public required ConPtySession Pty { get; init; }
        public readonly object Gate = new();
        public readonly Queue<byte[]> Chunks = new();
        public int BufferedBytes;
        public long TotalEmitted;
        public string? CurrentDirectory;
        public byte[] CwdCarry = [];
    }

    private readonly Dictionary<string, HostedSession> _sessions = [];
    private readonly object _sessionsGate = new();

    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;
    public event EventHandler<TerminalCwdEventArgs>? CurrentDirectoryChanged;
    public event EventHandler<string>? SessionStarted;
    public event EventHandler<string>? SessionClosed;

    public TerminalHostService()
    {
        // cmd.exe inherits PROMPT from our environment; it carries the same invisible
        // markers the PowerShell prompt hook emits (OSC 133 block marks + OSC 9;9 cwd),
        // plus $_ for the block-spacer row and #00FF00 truecolor on the visible prompt.
        // Other shells ignore it.
        Environment.SetEnvironmentVariable(
            "PROMPT",
            "$E]133;D$E\\$_$E]133;A$E\\$E]9;9;$P$E\\$E[38;2;0;255;0m$P$G$E[0m$E]133;B$E\\");

        // Git Bash runs PROMPT_COMMAND (inherited from the environment) before every
        // prompt: same block marks + spacer, and the cwd translated to a Windows path
        // via cygpath. A user bashrc that overwrites PROMPT_COMMAND simply loses the
        // integration, gracefully. WSL does not inherit Windows env vars, so it is
        // unaffected.
        Environment.SetEnvironmentVariable(
            "PROMPT_COMMAND",
            @"printf '\e]133;D\e\\\n\e]133;A\e\\\e]9;9;""%s""\e\\' ""$(cygpath -w ""$PWD"" 2>/dev/null || pwd)""");
    }

    public IReadOnlyList<string> ActiveSessionIds
    {
        get { lock (_sessionsGate) return _sessions.Keys.ToList(); }
    }

    public void StartSession(string sessionId, string shellPath, string arguments, string workingDirectory)
    {
        var commandLine = string.IsNullOrWhiteSpace(arguments)
            ? $"\"{shellPath}\""
            : $"\"{shellPath}\" {arguments}";
        var directory = Directory.Exists(workingDirectory)
            ? workingDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var pty = ConPtySession.Start(commandLine, directory, DefaultCols, DefaultRows);
        var session = new HostedSession { Pty = pty, CurrentDirectory = directory };

        lock (_sessionsGate)
            _sessions[sessionId] = session;

        pty.OutputReceived += (_, data) => Emit(sessionId, session, data);
        pty.Exited += (_, _) => Emit(sessionId, session,
            Encoding.UTF8.GetBytes("\r\n\x1b[90m[session ended]\x1b[0m\r\n"));

        SessionStarted?.Invoke(this, sessionId);
        pty.BeginOutput(); // only after subscribers are wired, so nothing is missed
    }

    public void SendInput(string sessionId, string data)
    {
        if (TryGet(sessionId, out var session))
            session.Pty.WriteInput(Encoding.UTF8.GetBytes(data));
    }

    public void Resize(string sessionId, int cols, int rows)
    {
        if (TryGet(sessionId, out var session))
            session.Pty.Resize(cols, rows);
    }

    public void CloseSession(string sessionId)
    {
        HostedSession? session;
        lock (_sessionsGate)
        {
            if (_sessions.Remove(sessionId, out session) is false)
                return;
        }

        session!.Pty.Dispose();
        SessionClosed?.Invoke(this, sessionId);
    }

    public (byte[] Data, long EndOffset) GetReplay(string sessionId)
    {
        if (!TryGet(sessionId, out var session))
            return ([], 0);

        lock (session.Gate)
        {
            var data = new byte[session.BufferedBytes];
            var position = 0;
            foreach (var chunk in session.Chunks)
            {
                chunk.CopyTo(data, position);
                position += chunk.Length;
            }
            return (data, session.TotalEmitted);
        }
    }

    public string? GetCurrentDirectory(string sessionId)
    {
        if (!TryGet(sessionId, out var session))
            return null;
        lock (session.Gate)
            return session.CurrentDirectory;
    }

    public void DisposeAll()
    {
        List<HostedSession> sessions;
        lock (_sessionsGate)
        {
            sessions = _sessions.Values.ToList();
            _sessions.Clear();
        }
        foreach (var session in sessions)
            session.Pty.Dispose();
    }

    private bool TryGet(string sessionId, out HostedSession session)
    {
        lock (_sessionsGate)
            return _sessions.TryGetValue(sessionId, out session!);
    }

    private void Emit(string sessionId, HostedSession session, byte[] data)
    {
        long offset;
        string? changedCwd;
        lock (session.Gate)
        {
            offset = session.TotalEmitted;
            session.TotalEmitted += data.Length;
            session.Chunks.Enqueue(data);
            session.BufferedBytes += data.Length;
            while (session.BufferedBytes > ReplayCapacity && session.Chunks.Count > 1)
                session.BufferedBytes -= session.Chunks.Dequeue().Length;

            changedCwd = ScanForCwd(session, data);
        }

        if (changedCwd is not null)
            CurrentDirectoryChanged?.Invoke(this, new TerminalCwdEventArgs(sessionId, changedCwd));

        OutputReceived?.Invoke(this, new TerminalOutputEventArgs(sessionId, data, offset));
    }

    /// <summary>
    /// Tracks the latest OSC 9;9 cwd marker in the output stream. Called with the
    /// session gate held; returns the new path when it differs from the last known
    /// one (so callers can raise a change event), else null. Sequences may split
    /// across chunks, so an incomplete match is carried over (bounded) and re-scanned.
    /// </summary>
    private static string? ScanForCwd(HostedSession session, byte[] chunk)
    {
        byte[] data;
        if (session.CwdCarry.Length > 0)
        {
            data = new byte[session.CwdCarry.Length + chunk.Length];
            session.CwdCarry.CopyTo(data, 0);
            chunk.CopyTo(data, session.CwdCarry.Length);
        }
        else
        {
            data = chunk;
        }

        var index = data.AsSpan().LastIndexOf(OscCwdPrefix);
        if (index < 0)
        {
            session.CwdCarry = KeepTail(data, OscCwdPrefix.Length - 1);
            return null;
        }

        var start = index + OscCwdPrefix.Length;
        var end = -1;
        var malformed = false;
        for (var i = start; i < data.Length; i++)
        {
            var b = data[i];
            if (b == 0x07) // BEL terminator
            {
                end = i;
                break;
            }
            if (b == 0x1B) // possibly the ESC of an ST (ESC \) terminator
            {
                if (i + 1 >= data.Length)
                    break; // split at chunk edge; need more data
                if (data[i + 1] == (byte)'\\')
                    end = i;
                else
                    malformed = true;
                break;
            }
        }

        if (end < 0)
        {
            session.CwdCarry = malformed || data.Length - index > 2048
                ? KeepTail(data, OscCwdPrefix.Length - 1)
                : data[index..];
            return null;
        }

        var path = Encoding.UTF8.GetString(data, start, end - start).Trim('"');
        session.CwdCarry = KeepTail(data, OscCwdPrefix.Length - 1);

        if (path.Length == 0 || !Directory.Exists(path) ||
            string.Equals(session.CurrentDirectory, path, StringComparison.OrdinalIgnoreCase))
            return null;

        session.CurrentDirectory = path;
        return path;
    }

    private static byte[] KeepTail(byte[] data, int count) =>
        data.Length <= count ? data : data[^count..];
}
