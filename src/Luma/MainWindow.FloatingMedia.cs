using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Luma;

public partial class MainWindow
{
    private FloatingVideoWindow? _floatingVideo;
    private BrowserTab? _floatingVideoTab;
    private CoreWebView2DevToolsProtocolEventReceiver? _screencastReceiver;
    private EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs>? _screencastHandler;
    private bool _screencastActive;
    private int _screencastFrameBusy;

    private static bool IsSupportedMusicService(BrowserTab? tab)
    {
        if (tab is null || !Uri.TryCreate(tab.ActiveUrl, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.TrimStart().TrimEnd('.').ToLowerInvariant();
        return host == "music.youtube.com" || host == "soundcloud.com" || host.EndsWith(".soundcloud.com", StringComparison.Ordinal)
            || host == "open.spotify.com";
    }

    private void HandleBrowserBackgrounded()
    {
        if (_closing) return;
        var current = CurrentTab;
        if (current is null) return;
        var actuallyFullscreen = false;
        try
        {
            var core = current.ActiveView?.CoreWebView2;
            actuallyFullscreen = core?.ContainsFullScreenElement == true;
        }
        catch (Exception ex) { App.Log(ex); }
        if (_fullscreen && !actuallyFullscreen) SetFullscreen(false);
        if (_state.FloatingVideoEnabled && actuallyFullscreen)
        {
            HideFloatingMusic();
            if (!_floatingVideoRequested)
            {
                _floatingVideoRequested = true;
                _ = OpenFloatingVideoAsync(current);
            }
            return;
        }
        RefreshFloatingMusic();
    }

    /// <summary>
    /// WebView2's Document-PiP surface is not embeddable reliably: it asks the host for an
    /// about:blank window and can leave it black. Instead, stream the already-rendering fullscreen
    /// page through Chromium's DevTools screencast and keep all controls connected to the original
    /// media element. This works for YouTube and cross-origin players because it captures the final
    /// composited WebView, not the site's DOM.
    /// </summary>
    private async Task OpenFloatingVideoAsync(BrowserTab tab)
    {
        CoreWebView2? core = null;
        try { core = tab.ActiveView?.CoreWebView2; } catch { }
        if (core is null) { _floatingVideoRequested = false; return; }
        try
        {
            if (_floatingVideo is not null && !_floatingVideo.IsClosed)
            {
                _floatingVideo.Update(tab);
                return;
            }
            _floatingVideo = null;

            _floatingVideoTab = tab;
            _floatingVideo = new FloatingVideoWindow(
                (action, value) => SendFloatingVideoCommand(tab, action, value),
                () => _ = StopFloatingVideoAsync(false));
            _floatingVideo.SyncTheme(this);
            _floatingVideo.Topmost = _state.FloatingMediaAlwaysOnTop;
            _floatingVideo.Update(tab);
            _floatingVideo.Show();

            _screencastReceiver = core.GetDevToolsProtocolEventReceiver("Page.screencastFrame");
            _screencastHandler = FloatingScreencastFrame;
            _screencastReceiver.DevToolsProtocolEventReceived += _screencastHandler;
            await core.CallDevToolsProtocolMethodAsync("Page.startScreencast", JsonSerializer.Serialize(new
            {
                format = "jpeg",
                quality = Math.Clamp(_state.FloatingVideoQuality, 45, 95),
                maxWidth = 1280,
                maxHeight = 720,
                everyNthFrame = 1,
            }));
            if (!_floatingVideoRequested || _floatingVideo is null)
            {
                try { await core.CallDevToolsProtocolMethodAsync("Page.stopScreencast", "{}"); } catch { }
                return;
            }
            _screencastActive = true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            await StopFloatingVideoAsync();
        }
    }

    private async void FloatingScreencastFrame(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        var core = _floatingVideoTab?.ActiveView.CoreWebView2;
        if (core is null) return;
        var ownsFrameSlot = false;
        try
        {
            using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            var root = doc.RootElement;
            var sessionId = root.TryGetProperty("sessionId", out var id) && id.TryGetInt32(out var parsed) ? parsed : 0;
            if (sessionId != 0)
                await core.CallDevToolsProtocolMethodAsync("Page.screencastFrameAck", JsonSerializer.Serialize(new { sessionId }));
            if (!root.TryGetProperty("data", out var encoded) || Interlocked.Exchange(ref _screencastFrameBusy, 1) != 0) return;
            ownsFrameSlot = true;
            var bytes = Convert.FromBase64String(encoded.GetString() ?? "");
            await Dispatcher.InvokeAsync(() => _floatingVideo?.SetFrame(bytes), DispatcherPriority.Render);
        }
        catch (Exception ex) { App.Log(ex); }
        finally { if (ownsFrameSlot) Interlocked.Exchange(ref _screencastFrameBusy, 0); }
    }

    private void SendFloatingVideoCommand(BrowserTab tab, string action, double value)
    {
        if (action == "return-fullscreen")
        {
            _ = ReturnFloatingVideoToLumaAsync(tab);
            return;
        }
        var core = tab.ActiveView.CoreWebView2;
        if (core is null) return;
        try { core.PostWebMessageAsJson(JsonSerializer.Serialize(new { kind = "luma-media-control", action, value })); }
        catch (Exception ex) { App.Log(ex); }
    }

    private async Task ReturnFloatingVideoToLumaAsync(BrowserTab tab)
    {
        var core = tab.ActiveView.CoreWebView2;
        await StopFloatingVideoAsync();
        await Dispatcher.InvokeAsync(() =>
        {
            if (!ReferenceEquals(CurrentTab, tab)) CurrentTab = tab;
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Focus();
            tab.ActiveView.Focus();
        });
        if (core is null) return;
        try
        {
            if (!core.ContainsFullScreenElement)
                await core.ExecuteScriptAsync("(()=>{const v=[...document.querySelectorAll('video')].find(x=>!x.paused)||document.querySelector('video');if(v&&v.requestFullscreen)v.requestFullscreen().catch(()=>{});})()");
            await Dispatcher.InvokeAsync(() => SetFullscreen(true));
        }
        catch (Exception ex) { App.Log(ex); }
    }

    private async Task StopFloatingVideoAsync(bool closeWindow = true)
    {
        var core = _floatingVideoTab?.ActiveView.CoreWebView2;
        if (_screencastReceiver is not null && _screencastHandler is not null)
            _screencastReceiver.DevToolsProtocolEventReceived -= _screencastHandler;
        _screencastReceiver = null;
        _screencastHandler = null;
        if (_screencastActive && core is not null)
        {
            try { await core.CallDevToolsProtocolMethodAsync("Page.stopScreencast", "{}"); }
            catch (Exception ex) { App.Log(ex); }
        }
        _screencastActive = false;
        Interlocked.Exchange(ref _screencastFrameBusy, 0);

        var window = _floatingVideo;
        _floatingVideo = null;
        _floatingVideoTab = null;
        _floatingVideoRequested = false;
        if (closeWindow && window is not null)
        {
            try { window.CloseFromOwner(); } catch { }
        }
    }

    private void CloseFloatingVideoForShutdown()
    {
        if (_screencastReceiver is not null && _screencastHandler is not null)
            _screencastReceiver.DevToolsProtocolEventReceived -= _screencastHandler;
        _screencastReceiver = null;
        _screencastHandler = null;
        if (_screencastActive && _floatingVideoTab?.ActiveView.CoreWebView2 is { } core)
            _ = core.CallDevToolsProtocolMethodAsync("Page.stopScreencast", "{}");
        _screencastActive = false;
        _floatingVideoRequested = false;
        var window = _floatingVideo;
        _floatingVideo = null;
        _floatingVideoTab = null;
        try { window?.CloseFromOwner(); } catch { }
    }

    private void RefreshFloatingMusic()
    {
        var backgrounded = WindowState == WindowState.Minimized || !IsActive;
        if (_floatingMusicDismissed || !_state.FloatingMusicEnabled || !backgrounded || _fullscreen || !IsSupportedMusicService(_nowPlaying))
        {
            HideFloatingMusic();
            return;
        }
        _floatingMusic ??= new FloatingMusicWindow((action, value) =>
        {
            if (action == "dismiss") { _floatingMusicDismissed = true; HideFloatingMusic(); return; }
            SendMediaCommand(action, value);
        });
        _floatingMusic.Topmost = _state.FloatingMediaAlwaysOnTop;
        _floatingMusic.SyncTheme(this);
        if (_nowPlaying is not null) _floatingMusic.Update(_nowPlaying);
        _floatingMusic.ShowWithoutActivation();
    }

    private void HideFloatingMusic()
    {
        if (_floatingMusic?.IsVisible == true) _floatingMusic.Hide();
    }
}
