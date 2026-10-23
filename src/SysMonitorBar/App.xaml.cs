using System.IO;
using System.Windows;
using SysMonitorBar.Core.Logging;
using SysMonitorBar.Core.Settings;
using SysMonitorBar.Infrastructure;

namespace SysMonitorBar;

public partial class App : Application
{
    private AppController? _controller;
    private RollingFileLog? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _log = new RollingFileLog(Path.Combine(SettingsService.DefaultDirectory, "logs"));
        Log.Current = _log;
        HookUnhandledExceptions();

        var guard = SingleInstanceGuard.TryAcquire();
        if (guard is null)
        {
            Log.Info("Another instance is running; asking it to show the bar and exiting.");
            SingleInstanceGuard.SignalExistingInstance();
            Shutdown();
            return;
        }

        var version = typeof(App).Assembly.GetName().Version;
        Log.Info($"SysMonitorBar {version} starting on {Environment.OSVersion} ({Environment.ProcessPath}).");

        var settingsService = new SettingsService(SettingsService.DefaultDirectory);
        SettingsLoadResult load;
        try
        {
            load = settingsService.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable (e.g. locked) file: run with defaults rather than not at all.
            Log.Warning("Could not read settings; using defaults.", ex);
            load = new SettingsLoadResult(AppSettings.CreateDefault(), SettingsLoadStatus.Missing);
        }
        Log.Info(load.Status == SettingsLoadStatus.Corrupt
            ? $"Settings file was corrupt; moved to {load.CorruptBackupPath} and using defaults."
            : $"Settings: {load.Status}.");

        var simulate = e.Args.Any(a => string.Equals(a, "--simulate", StringComparison.OrdinalIgnoreCase));
        _controller = new AppController(settingsService, guard, load.Settings, simulate);
        _controller.Start(load);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        Log.Info($"Session ending ({e.ReasonSessionEnding}).");
        // Best effort at logoff/shutdown; nothing here must block the session from ending.
        _ = _controller?.ExitAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Dispose();
        base.OnExit(e);
    }

    private void HookUnhandledExceptions()
    {
        // A monitor that silently dies is worse than one that logs and keeps going: UI-thread
        // exceptions are logged and swallowed; anything else is logged before the process ends.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled exception on the UI thread.", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception; the process will end.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Warning("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
    }
}
