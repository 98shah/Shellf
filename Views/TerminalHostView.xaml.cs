using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Shellf.Services;

namespace Shellf.Views;

/// <summary>
/// Hosts ONE WebView2 for the whole app; every terminal tab is an xterm.js instance
/// inside it. Bridges <see cref="ITerminalHostService"/> (ConPTY sessions) to the page:
/// output bytes go in as base64, keystrokes and resizes come back as web messages.
/// View models never touch this class — it binds only via <see cref="ActiveSessionId"/>.
/// </summary>
public partial class TerminalHostView : UserControl
{
    public static readonly DependencyProperty ActiveSessionIdProperty = DependencyProperty.Register(
        nameof(ActiveSessionId),
        typeof(string),
        typeof(TerminalHostView),
        new PropertyMetadata(string.Empty, (d, _) => ((TerminalHostView)d).ShowActiveTerminal()));

    public string ActiveSessionId
    {
        get => (string)GetValue(ActiveSessionIdProperty);
        set => SetValue(ActiveSessionIdProperty, value);
    }

    // How many bytes of each session's output stream have been written to xterm.js.
    // Used to skip event chunks already covered by the replay buffer.
    private readonly Dictionary<string, long> _writtenTo = [];

    private ITerminalHostService? _host;
    private bool _initStarted;
    private bool _webReady;

    public TerminalHostView()
    {
        InitializeComponent();
        Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x00, 0x00, 0x00);
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_initStarted)
            return;
        _initStarted = true;

        _host = ((App)Application.Current).Services.GetRequiredService<ITerminalHostService>();
        _host.SessionStarted += (_, id) => Dispatcher.InvokeAsync(() => CreateTerminal(id));
        _host.SessionClosed += (_, id) => Dispatcher.InvokeAsync(() => RemoveTerminal(id));
        _host.OutputReceived += (_, e) => Dispatcher.InvokeAsync(() => ForwardOutput(e));

        try
        {
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shellf", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
            await Web.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                "The WebView2 Runtime is required to render terminals.\n" +
                "Install it from https://developer.microsoft.com/microsoft-edge/webview2/",
                "Shellf", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var core = Web.CoreWebView2;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false; // Ctrl+W etc. belong to the shell
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.SetVirtualHostNameToFolderMapping(
            "shellf.assets",
            Path.Combine(AppContext.BaseDirectory, "Assets"),
            CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnWebMessage;
        core.ProcessFailed += (_, args) => Dispatcher.InvokeAsync(() => OnWebProcessFailed(args));
        core.Navigate("https://shellf.assets/terminal.html");
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var root = doc.RootElement;

        switch (root.GetProperty("type").GetString())
        {
            case "ready":
                _webReady = true;
                // Report the measured grid first: queued sessions spawn at the right
                // size (raising SessionStarted -> CreateTerminal for each).
                _host!.SetViewGridSize(
                    root.GetProperty("cols").GetInt32(),
                    root.GetProperty("rows").GetInt32());
                foreach (var id in _host.ActiveSessionIds)
                    CreateTerminal(id);
                ShowActiveTerminal();
                break;

            case "input":
                _host!.SendInput(
                    root.GetProperty("id").GetString()!,
                    root.GetProperty("data").GetString()!);
                break;

            case "resize":
                _host!.Resize(
                    root.GetProperty("id").GetString()!,
                    root.GetProperty("cols").GetInt32(),
                    root.GetProperty("rows").GetInt32());
                break;
        }
    }

    private void CreateTerminal(string sessionId)
    {
        if (!_webReady || _host is null || _writtenTo.ContainsKey(sessionId))
            return;

        var (data, endOffset) = _host.GetReplay(sessionId);
        _writtenTo[sessionId] = endOffset;
        Post(new { type = "create", id = sessionId, data = Convert.ToBase64String(data) });
    }

    private void ForwardOutput(TerminalOutputEventArgs e)
    {
        // Unknown session: its 'create' (with replay covering this chunk) hasn't run yet.
        if (!_webReady || !_writtenTo.TryGetValue(e.SessionId, out var writtenTo) || e.Offset < writtenTo)
            return;

        _writtenTo[e.SessionId] = e.Offset + e.Data.Length;
        Post(new { type = "write", id = e.SessionId, data = Convert.ToBase64String(e.Data) });
    }

    private void RemoveTerminal(string sessionId)
    {
        _writtenTo.Remove(sessionId);
        Post(new { type = "dispose", id = sessionId });
    }

    private void ShowActiveTerminal()
    {
        Post(new { type = "show", id = ActiveSessionId });
        if (_webReady && ActiveSessionId.Length > 0)
            Web.Focus();
    }

    /// <summary>Hands keyboard focus back to the terminal, e.g. after an inline rename.</summary>
    public void FocusTerminal()
    {
        if (_webReady && ActiveSessionId.Length > 0)
            Web.Focus();
    }

    /// <summary>
    /// WebView2 processes live outside ours and can die independently; that must
    /// never take the shells down with it.
    /// </summary>
    private void OnWebProcessFailed(CoreWebView2ProcessFailedEventArgs e)
    {
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                // The page is gone or hung but the browser process survives.
                // Reload: the page's "ready" handshake then recreates every
                // terminal from its replay buffer, which is why _writtenTo must
                // forget what the dead page had been sent.
                _webReady = false;
                _writtenTo.Clear();
                try
                {
                    Web.CoreWebView2.Reload();
                }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or COMException)
                {
                    // The browser process died in the meantime; handled below on
                    // its own ProcessFailed event.
                }
                break;

            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                // The whole runtime is gone and this CoreWebView2 is invalid; it
                // cannot be revived in place. Terminals go dark until the app is
                // relaunched, but the shells stay alive and the app keeps running.
                _webReady = false;
                _writtenTo.Clear();
                break;

            // GPU/utility/frame process failures: WebView2 recovers these itself.
        }
    }

    private void Post(object message)
    {
        if (!_webReady)
            return;

        try
        {
            Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or COMException)
        {
            // Core died between the ProcessFailed event and this post; stop
            // posting until a reload reports "ready" again.
            _webReady = false;
        }
    }
}
