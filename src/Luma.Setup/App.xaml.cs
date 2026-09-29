using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Luma.Setup;

public partial class App : Application
{
    private static readonly string CrashLog = Path.Combine(Path.GetTempPath(), "LumaSetup-crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, args) => { Log(args.Exception); args.SetObserved(); };
        try
        {
            MainWindow = new SetupWindow();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            ShowFatal(ex);
            Shutdown(-1);
        }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatal(e.Exception);
        e.Handled = true;
        Shutdown(-1);
    }

    private static void ShowFatal(Exception ex)
    {
        Log(ex);
        MessageBox.Show("Luma Setup could not start.\n\n" + ex.Message + "\n\nDiagnostics saved to:\n" + CrashLog,
            "Luma Setup", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void Log(Exception ex)
    {
        try { File.AppendAllText(CrashLog, $"[{DateTime.Now:O}]\n{ex}\n\n"); } catch { }
    }
}
