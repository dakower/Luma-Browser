using Luma.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;

namespace Luma;

public partial class App : System.Windows.Application
{
    static App()
    {
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(120));
    }

    private readonly BrowserServices _services = BrowserServices.CreateDefault();
    private SingleInstanceHost? _host;
    private Forms.NotifyIcon? _tray;
    private bool _exiting;
    private string? _updateSuccessMarker;
    private bool _ownsSessionMarker;

    public bool KeepInBackground { get; set; }
    public bool IsPrivateSession { get; private set; }
    public string PrivateDataPath { get; private set; } = string.Empty;
    public bool IsExiting => _exiting;
    public static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma", "crash.log");
    public static string PendingCrashPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma", "pending-crash.txt");
    public static string SessionMarkerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma", "running-session.marker");

    /// <summary>
    /// Cookies, logins, passwords and site data live here. Previously the browser passed no
    /// user-data folder at all, so WebView2 used its default: a folder next to Luma.exe inside
    /// the install directory. Every install and every update overwrites that directory (and the
    /// installer kills the running browser first), which is exactly why every account was gone
    /// after restarting. A fixed folder under LOCALAPPDATA survives updates and reinstalls.
    /// </summary>
    public static string ProfilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma", "Profile");

    /// <summary>Creates the profile folder and moves an old in-install-folder profile into it once.</summary>
    public static string EnsureProfilePath()
    {
        var path = ProfilePath;
        try
        {
            Directory.CreateDirectory(path);
            var target = Path.Combine(path, "EBWebView");
            if (!Directory.Exists(target))
            {
                var legacy = Path.Combine(AppContext.BaseDirectory, "Luma.exe.WebView2", "EBWebView");
                if (Directory.Exists(legacy)) CopyTree(legacy, target);
            }
        }
        catch (Exception ex) { Log(ex); }
        return path;
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            try { File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true); } catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception) RecordPendingCrash(exception);
            else try { Directory.CreateDirectory(Path.GetDirectoryName(PendingCrashPath)!); File.WriteAllText(PendingCrashPath, $"[{DateTime.Now:O}]\nНеизвестная критическая ошибка Luma."); } catch { }
        };
        base.OnStartup(e);
        if (e.Args.Contains("--check-webview2-runtime", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(WebViewRuntime.IsAvailable(out _) ? 0 : 1);
            return;
        }
        CleanupPrivateProfiles(); KeepInBackground = StartupManager.Enabled;
        if (!WebViewRuntime.EnsureAvailableAsync().GetAwaiter().GetResult())
        {
            System.Windows.MessageBox.Show("Не найден совместимый Microsoft Edge WebView2 Runtime.\n\nLuma попыталась автоматически восстановить компонент, но Windows не подтвердила установку. Запустите новый установщик Luma от имени администратора.", "Luma — требуется WebView2 Runtime", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        var markerArg = e.Args.FirstOrDefault(a => a.StartsWith("--update-complete-marker=", StringComparison.OrdinalIgnoreCase));
        _updateSuccessMarker = markerArg is null ? null : markerArg[(markerArg.IndexOf('=') + 1)..].Trim('"');
        IsPrivateSession = e.Args.Contains("--incognito");
        if (IsPrivateSession)
        {
            KeepInBackground = false;
            PrivateDataPath = Path.Combine(Path.GetTempPath(), "LumaPrivate", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(PrivateDataPath);
        }

        try
        {
            _host = new SingleInstanceHost(IsPrivateSession ? $"LumaBrowser.Private.{Environment.ProcessId}" : "LumaBrowser.SingleInstance.v182");
            if (!_host.IsPrimary)
            {
                _host.Send(e.Args);
                Shutdown();
                return;
            }

            if (!IsPrivateSession)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(SessionMarkerPath)!);
                    if (File.Exists(SessionMarkerPath) && !File.Exists(PendingCrashPath))
                    {
                        var previous = File.ReadAllText(SessionMarkerPath);
                        File.WriteAllText(PendingCrashPath, $"[{DateTime.Now:O}]\nПредыдущая сессия Luma не завершилась корректно.\nМаркер запуска: {previous}");
                    }
                    File.WriteAllText(SessionMarkerPath, $"PID={Environment.ProcessId}; started={DateTime.Now:O}");
                    _ownsSessionMarker = true;
                }
                catch (Exception ex) { Log(ex); }
            }

            _host.CommandReceived += args => Dispatcher.Invoke(() => { var forced = args.Contains("--show-welcome"); var target = args.FirstOrDefault(value => !value.StartsWith("--")); ShowBrowser(target, forced); });
            _host.StartListening();
            SetupTray();
            BrowserRegistry.RegisterPerUser(Process.GetCurrentProcess().MainModule!.FileName!);

            if (!e.Args.Contains("--background"))
            {
                var target = e.Args.FirstOrDefault(x => !x.StartsWith("--"));
                // --show-welcome replays onboarding, --reset-welcome clears the flag for the next run.
                var forced = e.Args.Contains("--show-welcome");
                if (e.Args.Contains("--reset-welcome"))
                {
                    _services.StateStore.State.HasSeenWelcome = false;
                    _services.StateStore.Save();
                }
                if (!IsPrivateSession && forced) ShowWelcomeThenBrowser(target);
                else ShowBrowser(target);
            }
        }
        catch (Exception ex)
        {
            Log(ex);
            System.Windows.MessageBox.Show($"Luma не удалось запустить.\n\n{ex.Message}\n\nЖурнал: {LogPath}", "Luma", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static void CleanupPrivateProfiles(){var r=Path.Combine(Path.GetTempPath(),"LumaPrivate");if(!Directory.Exists(r))return;foreach(var d in Directory.GetDirectories(r))try{if(Directory.GetCreationTimeUtc(d)<DateTime.UtcNow.AddHours(-6))Directory.Delete(d,true);}catch(Exception ex){Log(ex);}}

    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon { Text = "Luma Browser", Visible = true };
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule!.FileName!);
        if (icon is not null) _tray.Icon = icon;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть Luma", null, (_, _) => Dispatcher.Invoke(() => ShowBrowser(null)));
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(ExitCompletely));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => ShowBrowser(null));
    }

    /// <summary>
    /// Installer launch: play onboarding inside the browser body, then reveal the
    /// normal browser window. Any failure (missing asset, WebView2 trouble, user closing the window)
    /// falls through to the ordinary startup so onboarding can never block the browser.
    /// </summary>
    private void ShowWelcomeThenBrowser(string? target) => ShowBrowser(target, true);

    private void MarkWelcomeSeen()
    {
        try
        {
            _services.StateStore.State.HasSeenWelcome = true;
            _services.StateStore.Save();
        }
        catch (Exception ex) { Log(ex); }
    }

    public void ShowBrowser(string? target, bool showWelcome = false)
    {
        try
        {
            var window = Windows.OfType<MainWindow>().FirstOrDefault();
            if (window is null)
            {
                window = new MainWindow(_services);
                MainWindow = window;
                var marker = _updateSuccessMarker;
                if (!string.IsNullOrWhiteSpace(marker))
                {
                    EventHandler? rendered = null;
                    rendered = (_, _) => { window.ContentRendered -= rendered; try { Directory.CreateDirectory(Path.GetDirectoryName(marker)!); File.WriteAllText(marker, "ok"); _updateSuccessMarker = null; } catch (Exception ex) { Log(ex); } };
                    window.ContentRendered += rendered;
                }
                window.Show();
            }
            else
            {
                window.Show();
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
            if (showWelcome) _ = window.ShowWelcomeAsync(MarkWelcomeSeen);
            if (!string.IsNullOrWhiteSpace(target)) window.NavigateInput(target);
        }
        catch (Exception ex)
        {
            Log(ex);
            System.Windows.MessageBox.Show($"Не удалось открыть окно Luma.\n\n{ex.Message}\n\nЖурнал: {LogPath}", "Luma", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        RecordPendingCrash(e.Exception);
        System.Windows.MessageBox.Show($"Ошибка Luma:\n\n{e.Exception.Message}\n\nЖурнал: {LogPath}", "Luma", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void RecordPendingCrash(Exception exception)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(PendingCrashPath)!); File.WriteAllText(PendingCrashPath, $"[{DateTime.Now:O}]\n{exception}"); } catch { }
    }

    public static void Warn(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:O}] WARN {message}\n\n");
        }
        catch { }
    }

    public static void Log(Exception ex)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.AppendAllText(LogPath, $"[{DateTime.Now:O}]\n{ex}\n\n");
    }

    public void ExitCompletely()
    {
        _exiting = true;
        foreach (Window window in Windows) window.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSessionMarker)
        {
            try { File.Delete(SessionMarkerPath); } catch (Exception ex) { Log(ex); }
            _ownsSessionMarker = false;
        }
        _tray?.Dispose();
        _host?.Dispose();
        if (IsPrivateSession && !string.IsNullOrWhiteSpace(PrivateDataPath))
        {
            try { Directory.Delete(PrivateDataPath, true); } catch (Exception ex) { Log(ex); }
        }
        base.OnExit(e);
    }
}
