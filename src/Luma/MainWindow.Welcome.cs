using System.Windows;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace Luma;

public partial class MainWindow
{
    private Action? _embeddedWelcomeCompleted;
    private bool _embeddedWelcomeFinishing;

    internal async Task ShowWelcomeAsync(Action completed)
    {
        var html = WelcomeWindow.LoadHtml();
        if (string.IsNullOrWhiteSpace(html)) { completed(); return; }
        _embeddedWelcomeCompleted = completed;
        _embeddedWelcomeFinishing = false;
        WelcomeOverlay.Opacity = 1;
        WelcomeOverlay.Visibility = Visibility.Visible;
        WelcomeView.Visibility = Visibility.Visible;
        var theme = Registry.CurrentUser.OpenSubKey(@"Software\Luma")?.GetValue("Theme") as string ?? "dark";
        var accent = theme switch { "blue" => "#6687C8", "purple" => "#756BB7", "sand" => "#A77C64", "mint" => "#4F9E94", _ => "#7468C7" };
        html = html.Replace("__LUMA_ACCENT__", accent, StringComparison.Ordinal);
        try
        {
            await WelcomeView.EnsureCoreWebView2Async(await BrowserEnvironmentAsync());
            var web = WelcomeView.CoreWebView2;
            AttachWebInterfaceLocalization(WelcomeView);
            web.Settings.AreDefaultContextMenusEnabled = false;
            web.Settings.IsStatusBarEnabled = false;
            web.Settings.AreDevToolsEnabled = false;
            web.Settings.IsZoomControlEnabled = false;
            web.NewWindowRequested += (_, e) => e.Handled = true;
            web.WebMessageReceived += EmbeddedWelcomeMessage;
            await web.AddScriptToExecuteOnDocumentCreatedAsync("window.addEventListener('luma:welcome-done',()=>{try{chrome.webview.postMessage('luma-welcome-done')}catch(e){}});window.luma=window.luma||{};window.luma.completeOnboarding=()=>{try{chrome.webview.postMessage('luma-welcome-done')}catch(e){}};");
            WelcomeView.NavigateToString(html);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            FinishEmbeddedWelcome();
        }
    }

    private void EmbeddedWelcomeMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        => Dispatcher.Invoke(FinishEmbeddedWelcome);

    private void FinishEmbeddedWelcome()
    {
        if (_embeddedWelcomeFinishing) return;
        _embeddedWelcomeFinishing = true;
        if (WelcomeView.CoreWebView2 is not null) WelcomeView.CoreWebView2.WebMessageReceived -= EmbeddedWelcomeMessage;
        WelcomeView.Visibility = Visibility.Collapsed;
        WelcomeOverlay.Visibility = Visibility.Collapsed;
        var completed = _embeddedWelcomeCompleted;
        _embeddedWelcomeCompleted = null;
        completed?.Invoke();
        HomeSearchBox.Focus();
    }
}
