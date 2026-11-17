using System.IO;
using System.Windows;
using DocTwin.Diagnostics;
using Serilog;

namespace DocTwin;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try { AppDiagnostics.Initialize(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Local diagnostics are unavailable. Check access to your local DocTwin application-data folder. Operations remain available, but this session may have no support log.",
                "DocTwin diagnostics", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        DispatcherUnhandledException += (_, args) =>
        {
            AppDiagnostics.Unexpected(args.Exception, "Dispatcher");
            args.Handled = true;
            MessageBox.Show("An unexpected error occurred. DocTwin must close to avoid continuing with inconsistent state. Unsaved results may be lost. Restart and share only the content-free support log with your support team.",
                "DocTwin stopped", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception error) AppDiagnostics.Unexpected(error, "AppDomain");
        };
        Themes.ThemeService.Current.Initialize();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("ApplicationStopped {ExitCode}", e.ApplicationExitCode);
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
