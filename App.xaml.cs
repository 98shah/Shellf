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
    private ServiceProvider? _services;

    /// <summary>Used by views that XAML instantiates (e.g. TerminalHostView) to reach services.</summary>
    public IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("Services are not available before startup.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
}
