using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using WpfBrushes = System.Windows.Media.Brushes;
using Microsoft.Web.WebView2.Wpf;

namespace Luma;

/// <summary>
/// Fullscreen onboarding shown exactly once per installation, right after the setup wizard
/// hands over to the browser. The animation lives in Assets/welcome.html (embedded, offline)
/// and finishes by dispatching "luma:welcome-done" or calling window.luma.completeOnboarding().
/// </summary>
public sealed class WelcomeWindow : Window
{
    private const string ResourceName = "Luma.Assets.welcome.html";

    /// <summary>Preload bridge: both completion paths from the page end up as one host message.</summary>
    private const string Bridge = """
    (() => {
      let done = false;
      const finish = () => {
        if (done) return; done = true;
        try { chrome.webview.postMessage('luma-welcome-done'); } catch (e) {}
      };
      window.luma = window.luma || {};
      window.luma.completeOnboarding = finish;
      window.addEventListener('luma:welcome-done', finish);
    })();
    """;

    private readonly WebView2 _view = new();
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromSeconds(12) };
    private bool _completed;
    private bool _rendered;

    /// <summary>Raised once, when onboarding is done, dismissed or failed.</summary>
    public event Action? Completed;

    public WelcomeWindow()
    {
        Title = "Luma";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        ShowInTaskbar = true;
        Background = WpfBrushes.Black;
        Content = _view;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(); };
        Closed += (_, _) => Finish();
        // Hard safety net: if WebView2 never initialises or the animation never paints, hand over
        // to the normal browser instead of leaving the user staring at a black screen.
        _watchdog.Tick += (_, _) =>
        {
            _watchdog.Stop();
            if (_rendered) return;
            App.Warn("Onboarding did not render in time - opening the browser instead.");
            Finish();
        };
    }

    public static string? LoadHtml()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            var html = reader.ReadToEnd();
            return string.IsNullOrWhiteSpace(html) ? null : html;
        }
        catch (Exception ex) { App.Log(ex); return null; }
    }

    /// <summary>
    /// Loads the animation. Must be called after the window is shown: a WPF WebView2 that is not
    /// part of a visible window never finishes EnsureCoreWebView2Async, so initialising it first
    /// left the app hanging with no window at all.
    /// </summary>
    public async Task<bool> TryStartAsync()
    {
        var html = LoadHtml();
        if (html is null) { App.Warn("welcome.html is missing or empty - skipping onboarding."); return false; }
        var theme = Registry.CurrentUser.OpenSubKey(@"Software\Luma")?.GetValue("Theme") as string ?? "dark";
        var accent = theme switch { "blue" => "#6687C8", "purple" => "#756BB7", "sand" => "#A77C64", "mint" => "#4F9E94", _ => "#7468C7" };
        html = html.Replace("__LUMA_ACCENT__", accent, StringComparison.Ordinal);
        try
        {
            _watchdog.Start();
            var environment = await MainWindow.BrowserEnvironmentAsync();
            await _view.EnsureCoreWebView2Async(environment);
            var web = _view.CoreWebView2;
            web.Settings.AreDefaultContextMenusEnabled = false;
            web.Settings.IsStatusBarEnabled = false;
            web.Settings.AreDevToolsEnabled = false;
            web.Settings.IsZoomControlEnabled = false;
            web.NewWindowRequested += (_, e) => e.Handled = true;
            web.WebMessageReceived += (_, _) => Dispatcher.Invoke(Finish);
            web.NavigationCompleted += (_, e) => Dispatcher.Invoke(() =>
            {
                if (e.IsSuccess) { _rendered = true; _watchdog.Stop(); }
                else { App.Warn($"welcome.html failed to render ({e.WebErrorStatus}) - skipping onboarding."); Finish(); }
            });
            await web.AddScriptToExecuteOnDocumentCreatedAsync(Bridge);
            await web.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.Scrollbars(accent));
            _view.NavigateToString(html);
            return true;
        }
        catch (Exception ex) { App.Log(ex); return false; }
    }

    /// <summary>Ends onboarding from the outside (startup failure, shutdown, etc.).</summary>
    public void Cancel() => Finish();

    private void Finish()
    {
        if (_completed) return;
        _completed = true;
        _watchdog.Stop();
        Completed?.Invoke();
        try { Close(); } catch { }
        try { _view.Dispose(); } catch { }
    }
}
