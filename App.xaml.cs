using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Shellf.Services;
using Shellf.ViewModels;

namespace Shellf;

/// <summary>
/// Entry point: builds the DI container and shows the main window immediately —
/// no splash screen, no boot logic.
/// </summary>
public partial class App : Application
{
    // Next to workspace.json, so a bug report can include both.
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shellf", "crash.log");

    private ServiceProvider? _services;

    /// <summary>Used by views that XAML instantiates (e.g. TerminalHostView) to reach services.</summary>
    public IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("Services are not available before startup.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A UI-thread throw is logged and swallowed: losing one interaction beats
        // tearing down every live shell. The other two sources cannot be recovered,
        // but at least they leave a stack trace behind.
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("UI thread", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash("Unhandled", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("Background task", args.Exception);
            args.SetObserved();
        };

        _services = new ServiceCollection()
            .AddSingleton<IWorkspaceStorageService, WorkspaceStorageService>()
            .AddSingleton<IShellCatalogService, ShellCatalogService>()
            .AddSingleton<ITerminalHostService, TerminalHostService>()
            .AddSingleton<IDialogService, Views.DialogService>()
            .AddSingleton<MainWindowViewModel>()
            .AddSingleton<MainWindow>()
            .BuildServiceProvider();

        MainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Shell processes must not outlive the app.
        _services?.GetRequiredService<ITerminalHostService>().DisposeAll();
        _services?.Dispose();
        base.OnExit(e);
    }

    private static void LogCrash(string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(
                CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // The logger itself must never take the app down.
        }
    }
}
