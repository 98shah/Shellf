using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static Shellf.Services.ConPty.ConPtyNative;

namespace Shellf.Services.ConPty;

/// <summary>
/// A shell attached to a Windows pseudo console. The shell believes it runs on a real
/// terminal, so PSReadLine, tab completion, colors, clear and full-screen apps all work.
/// Raw VT bytes flow out via <see cref="OutputReceived"/> (thread-pool thread); raw
/// keystrokes/escape sequences go in via <see cref="WriteInput"/>.
/// </summary>
public sealed class ConPtySession : IDisposable
{
    private readonly IntPtr _console;
    private readonly FileStream _inputWriter;
    private readonly FileStream _outputReader;
    private readonly Process _process;
    private readonly object _writeLock = new();
    private volatile bool _disposed;

    public event EventHandler<byte[]>? OutputReceived;
    public event EventHandler? Exited;

    private ConPtySession(IntPtr console, FileStream inputWriter, FileStream outputReader, Process process)
    {
        _console = console;
        _inputWriter = inputWriter;
        _outputReader = outputReader;
        _process = process;
    }

    public static ConPtySession Start(string commandLine, string workingDirectory, int cols, int rows)
    {
        if (!CreatePipe(out var ptyInRead, out var ptyInWrite, IntPtr.Zero, 0))
            throw new Win32Exception();
        if (!CreatePipe(out var ptyOutRead, out var ptyOutWrite, IntPtr.Zero, 0))
            throw new Win32Exception();

        var hr = CreatePseudoConsole(
            new COORD { X = (short)cols, Y = (short)rows }, ptyInRead, ptyOutWrite, 0, out var console);
        if (hr != 0)
            throw new Win32Exception(hr, "CreatePseudoConsole failed.");

        // The pseudo console duplicated its ends of the pipes; ours are all we keep.
        ptyInRead.Dispose();
        ptyOutWrite.Dispose();

        Process process;
        try
        {
            process = SpawnAttachedProcess(commandLine, workingDirectory, console);
        }
        catch
        {
            ClosePseudoConsole(console);
            ptyInWrite.Dispose();
            ptyOutRead.Dispose();
            throw;
        }

        return new ConPtySession(
            console,
            new FileStream(ptyInWrite, FileAccess.Write),
            new FileStream(ptyOutRead, FileAccess.Read),
            process);
    }

    /// <summary>
    /// Starts pumping output and exit notifications. Called by the owner after it has
    /// subscribed to the events, so the shell's very first output is never missed.
    /// </summary>
    public void BeginOutput()
    {
        _process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
        _process.EnableRaisingEvents = true; // fires immediately if it already exited

        _ = Task.Run(() =>
        {
            var buffer = new byte[8192];
            try
            {
                while (true)
                {
                    var read = _outputReader.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                        break;
                    OutputReceived?.Invoke(this, buffer[..read]);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Pipe closed during teardown.
            }
        });
    }

    public void WriteInput(byte[] data)
    {
        if (_disposed)
            return;

        lock (_writeLock)
        {
            try
            {
                _inputWriter.Write(data);
                _inputWriter.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Shell already gone; Exited has informed the UI.
            }
        }
    }

    public void Resize(int cols, int rows)
    {
        if (_disposed || cols < 1 || rows < 1)
            return;
        ResizePseudoConsole(_console, new COORD { X = (short)cols, Y = (short)rows });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited between the check and the kill.
        }

        ClosePseudoConsole(_console); // also unblocks the read loop
        _inputWriter.Dispose();
        _outputReader.Dispose();
        _process.Dispose();
    }

    private static Process SpawnAttachedProcess(string commandLine, string workingDirectory, IntPtr console)
    {
        var attrListSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize); // sizing call, expected to "fail"

        var attrList = Marshal.AllocHGlobal(attrListSize);
        try
        {
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref attrListSize))
                throw new Win32Exception();

            try
            {
                // For this attribute the HPCON itself is passed as the value pointer.
                if (!UpdateProcThreadAttribute(
                        attrList, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, console,
                        (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception();

                var startupInfo = new STARTUPINFOEXW
                {
                    StartupInfo = { cb = Marshal.SizeOf<STARTUPINFOEXW>() },
                    lpAttributeList = attrList,
                };

                if (!CreateProcessW(
                        null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                        EXTENDED_STARTUPINFO_PRESENT, IntPtr.Zero, workingDirectory,
                        ref startupInfo, out var processInfo))
                    throw new Win32Exception();

                CloseHandle(processInfo.hThread);
                var process = Process.GetProcessById(processInfo.dwProcessId);
                CloseHandle(processInfo.hProcess);
                KillOnCloseJob.Assign(process);
                return process;
            }
            finally
            {
                DeleteProcThreadAttributeList(attrList);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(attrList);
        }
    }
}
