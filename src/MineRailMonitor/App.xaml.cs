using System.Configuration;
using System.Windows;
using System.Windows.Threading;
using MineRailMonitor.Core.Acceptance;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Infrastructure.Logging;
using MineRailMonitor.Pages;

namespace MineRailMonitor;

public partial class App : Application
{
    public App()
    {
        AcceptanceOptions = AcceptanceCommandLineOptions.Parse(Environment.GetCommandLineArgs());
        var logDirectory = AcceptanceOptions.Enabled
            ? AcceptanceOptions.LogDirectory!
            : System.IO.Path.Combine(AppContext.BaseDirectory, "Logs");
        Logger = new FileLogger(logDirectory);
        AdminModeService = new AdminModeService(ReadAdminPassword());
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public ILogger Logger { get; }

    public AcceptanceCommandLineOptions AcceptanceOptions { get; }

    public AdminModeService AdminModeService { get; }

    private static string? ReadAdminPassword()
    {
        var configuredPassword = ConfigurationManager.AppSettings["AdminPassword"];
        if (!string.IsNullOrWhiteSpace(configuredPassword))
        {
            return configuredPassword;
        }

        return Environment.GetEnvironmentVariable("MINE_RAIL_ADMIN_PASSWORD");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Logger.Information("MineRailMonitor 启动。");
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Information("MineRailMonitor 退出。");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("UI 未处理异常。", e.Exception);
        var dialog = new StyledMessageDialog(
            "程序错误",
            $"程序遇到未处理错误：{e.Exception.Message}",
            MessageDialogKind.Error);
        if (MainWindow is not null)
        {
            dialog.Owner = MainWindow;
        }
        dialog.ShowDialog();
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Logger.Error("程序未处理异常。", exception);
        }
    }
}
