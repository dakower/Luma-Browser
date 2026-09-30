using Luma.Core;
using Luma.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Cursors = System.Windows.Input.Cursors;
using DataObject = System.Windows.DataObject;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using IDataObject = System.Windows.IDataObject;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using IconPath = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace Luma;

public partial class MainWindow
{
    private readonly Dictionary<WebView2, string> _networkErrorUrls = new();
    private readonly Dictionary<WebView2, string> _pendingNavigationUrls = new();
    // Tracks the newest navigation per view so late "failed" events of a navigation that was
    // already replaced (redirect, new click, reload, download) never produce an error page.
    private readonly Dictionary<WebView2, ulong> _latestNavigationIds = new();
    private readonly Dictionary<WebView2, (string Url, int Count)> _transientRetries = new();
    private readonly Dictionary<WebView2, DateTime> _lastDownloadStartedUtc = new();

    /// <summary>Drops per-view bookkeeping so closed tabs are not kept alive by these dictionaries.</summary>
    private void ForgetViewState(WebView2? view)
    {
        if (view is null) return;
        _networkErrorUrls.Remove(view); _pendingNavigationUrls.Remove(view);
        _latestNavigationIds.Remove(view); _transientRetries.Remove(view); _lastDownloadStartedUtc.Remove(view);
    }

    private static bool IsHardNetworkError(CoreWebView2WebErrorStatus status) => status is
        CoreWebView2WebErrorStatus.CannotConnect
        or CoreWebView2WebErrorStatus.HostNameNotResolved
        or CoreWebView2WebErrorStatus.Disconnected
        or CoreWebView2WebErrorStatus.Timeout
        or CoreWebView2WebErrorStatus.ServerUnreachable
        or CoreWebView2WebErrorStatus.ConnectionReset
        or CoreWebView2WebErrorStatus.ConnectionAborted;

    private static async Task<bool> PageHasRenderedContentAsync(WebView2 view)
    {
        if (view?.CoreWebView2 is null) return false;
        try
        {
            var res = await view.CoreWebView2.ExecuteScriptAsync(
                "Boolean(document.body && (document.body.innerText.trim().length > 40 || document.querySelectorAll('img, video, iframe, canvas, div, p').length > 5))");
            return res == "true";
        }
        catch { return false; }
    }

    internal async Task<BrowserTab> AddTabAsync(string input, bool activate = true, string? folderId = null, bool pinned = false)
    {
        var url = Normalize(input);
        if (TryParseLumaSearchUrl(url, out var searchQuery, out var searchMode))
            return await OpenLumaSearchTabAsync(searchQuery, searchMode, activate);
        var tab = new BrowserTab { FullUrl = url, Title = Domain(url), FolderId = folderId, IsPinned = pinned };
        _spaces[_activeSpace].Tabs.Add(tab);
        RecordTesterAction("Открыта новая вкладка");
        if (activate) CurrentTab = tab;
        await ConfigureViewAsync(tab, tab.View, url);
        FilterTabs();
        if (activate && ReferenceEquals(CurrentTab, tab))
        {
            // ConfigureViewAsync may finish after the user has already chosen another tab.
            // Never let that stale continuation reclaim selection or keyboard focus.
            UpdateChrome();
            RenderPanes();
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(CurrentTab, tab)) { tab.ActiveView.Focus(); Keyboard.Focus(tab.ActiveView); }
            });
        }
        return tab;
    }
    // A single shared environment for every tab: creating one per WebView2 was the main
    // source of stutter when opening tabs, and it also let Chromium throttle background
    // rendering. The flags keep compositing running while the window is partly covered.
    internal static string ChromiumVersion { get; } = DetectChromiumVersion();
    internal static string ChromeUserAgent { get; } = $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{ChromiumVersion} Safari/537.36";
    private static string DetectChromiumVersion()
    {
        try
        {
            var raw = CoreWebView2Environment.GetAvailableBrowserVersionString();
            var value = (raw ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (Version.TryParse(value, out var version) && version.Major >= 120)
                return $"{version.Major}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}.{Math.Max(0, version.Revision)}";
        }
        catch (Exception ex) { App.Log(ex); }
        // Used only while the Evergreen runtime is being repaired. Normal browsing always
        // reports the real installed Chromium version instead of a permanently frozen one.
        return "140.0.0.0";
    }
    private static readonly SemaphoreSlim EnvironmentGate = new(1, 1);
    private static CoreWebView2Environment? _sharedEnvironment;
    private static string _configuredBrowserLanguage = System.Globalization.CultureInfo.CurrentUICulture.Name;
    internal static async Task<CoreWebView2Environment?> BrowserEnvironmentAsync()
    {
        var app = (App)Application.Current;
        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--disable-features=CalculateNativeWinOcclusion --renderer-process-limit=4 --enable-features=TurnOffStreamingMediaWithBackgroundTab,ResourceScheduler,ProcessPerSite --js-flags=\"--max-old-space-size=512\" --disk-cache-size=104857600 --enable-smooth-scrolling --autoplay-policy=no-user-gesture-required --enable-usermedia-screen-capturing",
            Language = ResolveBrowserLanguage(),
        };
        await EnvironmentGate.WaitAsync();
        try
        {
            if (app.IsPrivateSession) return await CoreWebView2Environment.CreateAsync(null, app.PrivateDataPath, options);
            // Always pin the profile folder: the WebView2 default sits inside the install
            // directory, which the installer replaces on every update.
            return _sharedEnvironment ??= await CoreWebView2Environment.CreateAsync(null, App.EnsureProfilePath(), options);
        }
        catch (Exception first)
        {
            App.Log(first);
            if (await WebViewRuntime.EnsureAvailableAsync())
            {
                try
                {
                    if (app.IsPrivateSession) return await CoreWebView2Environment.CreateAsync(null, app.PrivateDataPath, options);
                    return _sharedEnvironment ??= await CoreWebView2Environment.CreateAsync(null, App.EnsureProfilePath(), options);
                }
                catch (Exception retry) { App.Log(retry); }
            }
            return null;
        }
        finally { EnvironmentGate.Release(); }
    }
    private static string ResolveBrowserLanguage()
    {
        return _configuredBrowserLanguage;
    }
    private static void ConfigureBrowserLanguage(string? language) => _configuredBrowserLanguage = NormalizeInterfaceLanguage(language);
    private BrowserTab AddTabShellToSpace(RuntimeSpace space, TabSessionState saved)
    {
        var id = string.IsNullOrWhiteSpace(saved.Id) ? Guid.NewGuid().ToString("N") : saved.Id;
        var tab = new BrowserTab
        {
            Id = id, FullUrl = saved.Url, Title = string.IsNullOrWhiteSpace(saved.Title) ? Domain(saved.Url) : saved.Title,
            FolderId = saved.FolderId, IsPinned = saved.IsPinned, IsInternal = saved.IsInternal,
            InternalPageKind = saved.InternalPageKind, SplitRatio = saved.SplitRatio
        };
        space.Tabs.Add(tab);
        return tab;
    }

    private async Task EnsureTabReadyAsync(BrowserTab tab)
    {
        if (tab.IsHome || tab.View.CoreWebView2 is not null) return;
        if (_tabInitializationTasks.TryGetValue(tab.Id, out var existing)) { await existing; return; }
        var initialization = InitializeCoreAsync();
        _tabInitializationTasks[tab.Id] = initialization;
        try { await initialization; }
        finally
        {
            if (_tabInitializationTasks.TryGetValue(tab.Id, out var active) && ReferenceEquals(active, initialization))
                _tabInitializationTasks.Remove(tab.Id);
        }

        async Task InitializeCoreAsync()
        {
            if (_pendingTabRestores.TryGetValue(tab.Id, out var saved))
            {
                await InitializeSavedTabAsync(tab, saved);
                _pendingTabRestores.Remove(tab.Id);
                return;
            }
            if (tab.IsInternal && TryParseLumaSearchUrl(tab.InternalPageKind.Length > 0 ? tab.InternalPageKind : tab.FullUrl, out var query, out var mode))
                await ConfigureLumaSearchTabAsync(tab, query, mode);
            else await ConfigureViewAsync(tab, tab.View, tab.FullUrl);
        }
    }

    private async Task InitializeSavedTabAsync(BrowserTab tab, TabSessionState saved)
    {
        if (tab.IsHome) return;
        if (tab.IsInternal && TryParseLumaSearchUrl(saved.InternalPageKind.Length > 0 ? saved.InternalPageKind : saved.Url, out var searchQuery, out var searchMode))
        {
            await ConfigureLumaSearchTabAsync(tab, searchQuery, searchMode);
            return;
        }
        await ConfigureViewAsync(tab, tab.View, saved.Url);
        if (saved.IsSplit && !string.IsNullOrWhiteSpace(saved.SecondaryUrl))
        {
            await EnableSplitAsync(tab, saved.SecondaryUrl, false);
            tab.SecondaryTitle = string.IsNullOrWhiteSpace(saved.SecondaryTitle) ? tab.SecondaryTitle : saved.SecondaryTitle;
            tab.ActivePane = saved.ActivePane;
        }
    }

    private BrowserTab EnsureHomeTab(RuntimeSpace space)
    {
        var existing = space.Tabs.FirstOrDefault(t => t.IsHome);
        if (existing is not null) return existing;
        var home = new BrowserTab { FullUrl = "luma://home", Title = "Luma", IsInternal = true, InternalPageKind = "luma://home" };
        space.Tabs.Insert(0, home);
        return home;
    }

    private bool _showAccountSurface;

    private void ShowHomeSurface(bool account)
    {
        _showAccountSurface = account;
        HomeSurfaceContent.Visibility = account ? Visibility.Collapsed : Visibility.Visible;
        AccountSurfaceScroll.Visibility = account ? Visibility.Visible : Visibility.Collapsed;
        AccountSurfaceCloseButton.Visibility = account ? Visibility.Visible : Visibility.Collapsed;
        if (account) RefreshAccountSurface();
        else
        {
            UpdateHomeRecentSites();
            Dispatcher.BeginInvoke(() => HomeSearchBox.Focus());
        }
    }

    private void OpenHomePage(bool account = false)
    {
        if (_spaces.Count == 0) return;
        _showAccountSurface = account;
        var home = EnsureHomeTab(_spaces[_activeSpace]);
        CurrentTab = home;
        ShowHomeSurface(account);
        if (account) AnimateAccountSurface();
        Save();
    }

    private void OpenNewHomeTab()
    {
        if (_spaces.Count == 0) return;
        _showAccountSurface = false;
        var home = new BrowserTab { FullUrl = "luma://home", Title = "Luma", IsInternal = true, InternalPageKind = "luma://home" };
        _spaces[_activeSpace].Tabs.Add(home);
        RecordTesterAction("Открыта новая вкладка");
        CurrentTab = home;
        HomeSearchBox.Clear();
        ShowHomeSurface(false);
        FilterTabs();
        Save();
    }

    private async void HomeSearchSubmit_Click(object sender, RoutedEventArgs e)
    {
        var value = HomeSearchBox.Text.Trim();
        if (value.Length == 0) { HomeSearchBox.Focus(); return; }
        var previousHome = CurrentTab is { IsHome: true } home ? home : null;
        var target = Normalize(value);
        if (TryParseLumaSearchUrl(target, out var query, out var mode)) await OpenLumaSearchTabAsync(query, mode);
        else await AddTabAsync(target);
        if (previousHome is not null && !ReferenceEquals(CurrentTab, previousHome))
        {
            _spaces[_activeSpace].Tabs.Remove(previousHome);
            FilterTabs(); Save();
        }
    }

    private void HomeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => HomeSearchPlaceholder.Visibility = string.IsNullOrEmpty(HomeSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateHomeRecentSites()
    {
        var buttons = new[] { HomeRecentButton1, HomeRecentButton2, HomeRecentButton3, HomeRecentButton4 };
        var titles = new[] { HomeRecentTitle1, HomeRecentTitle2, HomeRecentTitle3, HomeRecentTitle4 };
        var domains = new[] { HomeRecentDomain1, HomeRecentDomain2, HomeRecentDomain3, HomeRecentDomain4 };
        var icons = new[] { HomeRecentIcon1, HomeRecentIcon2, HomeRecentIcon3, HomeRecentIcon4 };
        var recent = new List<HistoryEntry>();
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _state.History.OrderByDescending(item => item.VisitedAt))
        {
            if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !hosts.Add(uri.Host)) continue;
            recent.Add(entry);
            if (recent.Count == 4) break;
        }
        for (var i = 0; i < buttons.Length; i++)
        {
            var visible = i < recent.Count;
            buttons[i].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible) { buttons[i].Tag = null; icons[i].Source = null; continue; }
            var entry = recent[i];
            buttons[i].Tag = entry.Url;
            titles[i].Text = string.IsNullOrWhiteSpace(entry.Title) ? entry.Domain : entry.Title;
            domains[i].Text = entry.Domain;
            icons[i].Visibility = Visibility.Visible;
           
            try { icons[i].Source = new BitmapImage(new Uri(entry.FaviconUrl)); }
            catch { icons[i].Source = null; icons[i].Visibility = Visibility.Collapsed; }
        }
        HomeRecentSitesGrid.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void HomeRecentSite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && url.Length > 0) await AddTabAsync(url);
    }

    private void HomeRecentIcon_Failed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image image)
        {
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
        }
        e.Handled = true;
    }

    private void HomeSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        HomeSearchSubmit_Click(sender, e);
    }
    /// <summary>Sites that legitimately need the camera, the microphone and a full tab.</summary>
    internal static bool IsConferencingHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        host = host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        foreach (var known in new[] { "meet.google.com", "classroom.google.com", "zoom.us", "teams.microsoft.com", "teams.live.com", "whereby.com", "discord.com", "webinar.ru", "jitsi.org", "meet.jit.si" })
            if (host.Equals(known, StringComparison.Ordinal) || host.EndsWith("." + known, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool IsCompatibilitySensitiveHost(string host)
    {
        host = (host ?? "").Trim('.').ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        foreach (var known in new[] { "naurok.com.ua", "notion.com", "notion.so", "google.com", "gstatic.com", "hcaptcha.com", "cloudflare.com", "challenges.cloudflare.com" })
            if (host.Equals(known, StringComparison.Ordinal) || host.EndsWith("." + known, StringComparison.Ordinal)) return true;
        return false;
    }

    private string CurrentAccentCss()
    {
        var color = Resources["AccentBrush"] is SolidColorBrush brush ? brush.Color : Color.FromRgb(0x76, 0x56, 0xE8);
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private async Task ConfigureViewAsync(BrowserTab tab, WebView2 view, string url)
    {
        view.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 16, 16, 21);
        try
        {
            await view.EnsureCoreWebView2Async(await BrowserEnvironmentAsync());
            var web = view.CoreWebView2;
            web.Settings.IsStatusBarEnabled = false; web.Settings.AreDefaultContextMenusEnabled = false;
            web.Settings.IsBuiltInErrorPageEnabled = false;
            web.Settings.UserAgent = ChromeUserAgent;
            web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            web.WebResourceRequested += (_, requestArgs) =>
            {
                try
                {
                    if (_state.DoNotTrack) requestArgs.Request.Headers.SetHeader("DNT", "1"); else requestArgs.Request.Headers.SetHeader("DNT", "0");
                    var major = ChromiumVersion.Split('.')[0];
                    requestArgs.Request.Headers.SetHeader("sec-ch-ua", $"\"Not?A_Brand\";v=\"8\", \"Chromium\";v=\"{major}\", \"Google Chrome\";v=\"{major}\"");
                    requestArgs.Request.Headers.SetHeader("sec-ch-ua-mobile", "?0");
                    requestArgs.Request.Headers.SetHeader("sec-ch-ua-platform", "\"Windows\"");
                }
                catch (Exception ex) { App.Log(ex); }
            };
            try { web.Profile.PreferredTrackingPreventionLevel = _state.BlockThirdPartyCookies ? CoreWebView2TrackingPreventionLevel.Strict : CoreWebView2TrackingPreventionLevel.Balanced; }
            catch (Exception ex) { App.Log(ex); }
            web.Settings.IsPasswordAutosaveEnabled = !((App)Application.Current).IsPrivateSession;
            web.Settings.IsGeneralAutofillEnabled = !((App)Application.Current).IsPrivateSession;
            // Any site (not just YouTube) that puts an element in fullscreen folds the whole
            // Luma chrome away and takes over the monitor.
            web.ContainsFullScreenElementChanged += (_, _) => Dispatcher.Invoke(() =>
            {
                if (tab != CurrentTab || !ReferenceEquals(tab.ActiveView, view)) return;
                SetFullscreen(web.ContainsFullScreenElement);
            });
            // Without a permission handler WebView2 quietly refuses the microphone and the
            // camera, which is exactly why a Meet call never finished loading.
            web.PermissionRequested += (_, args) =>
            {
                var host = Uri.TryCreate(args.Uri, UriKind.Absolute, out var permissionUri) ? permissionUri.Host : "";
                switch (args.PermissionKind)
                {
                    case CoreWebView2PermissionKind.Microphone:
                    case CoreWebView2PermissionKind.Camera:
                        if (IsConferencingHost(host) || args.IsUserInitiated)
                        {
                            args.State = CoreWebView2PermissionState.Allow;
                            args.Handled = true;
                        }
                        else
                        {
                            args.State = CoreWebView2PermissionState.Default;
                        }
                        break;
                    case CoreWebView2PermissionKind.Autoplay:
                        args.State = CoreWebView2PermissionState.Allow;
                        args.Handled = true;
                        break;
                }
            };
            // Google checks the browser brand, not just the user agent string, and refuses to
            // start a call when it sees "Microsoft Edge WebView2".
            await web.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.ClientHints(ChromiumVersion));
            await web.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.Scrollbars(CurrentAccentCss()));
            await web.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.ContextMenu(ReferenceEquals(tab.SecondaryView, view) ? "secondary" : "primary"));
            await web.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.TranslationCleanup);
            await web.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.LinkPrefetch);
            await web.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.MediaMemory);
            await web.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.MediaWatch);
            await web.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.DarkAuto(_state.ForceDarkDomains));
            web.WebMessageReceived += (_, e) => ReceiveWebMessage(tab, view, e.WebMessageAsJson);
            web.IsDocumentPlayingAudioChanged += async (_, _) => await SyncDocumentAudioStateAsync(tab, view);
            if (tab != CurrentTab) web.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            web.NavigationStarting += (_, args) => Dispatcher.Invoke(() =>
            {
                tab.IsNavigating = true;
                _latestNavigationIds[view] = args.NavigationId;
                ResetPageTranslation(view);
                try
                {
                    // Strict tracking prevention can block challenge cookies on login/exam
                    // pages. Compatibility-sensitive sites use Edge's Balanced level; the
                    // security challenge itself is never bypassed or auto-completed.
                    if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var compatibilityUri) && IsCompatibilitySensitiveHost(compatibilityUri.Host))
                        web.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.Balanced;
                    else
                        web.Profile.PreferredTrackingPreventionLevel = _state.BlockThirdPartyCookies ? CoreWebView2TrackingPreventionLevel.Strict : CoreWebView2TrackingPreventionLevel.Balanced;
                }
                catch (Exception ex) { App.Log(ex); }
                if (args.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)) _pendingNavigationUrls[view] = args.Uri;
                if (_networkErrorUrls.ContainsKey(view) && args.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    _networkErrorUrls.Remove(view);
                CloseTransientUi();
                // A pinned tab is bound to its site. Following a link to a different site hands
                // the page to a fresh normal tab instead of dragging the pin somewhere else.
                if (!tab.IsPinned || ReferenceEquals(tab.SecondaryView, view) || args.IsRedirected || !args.IsUserInitiated) return;
                var target = args.Uri;
                if (string.IsNullOrEmpty(target) || !target.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
                if (SameSite(target, tab.FullUrl)) return;
                args.Cancel = true;
                _ = Dispatcher.BeginInvoke(async () => await AddTabAsync(target));
            });
            web.SourceChanged += (_, _) => Dispatcher.Invoke(() =>
            {
                var current = tab.IsInternal && !string.IsNullOrWhiteSpace(tab.InternalPageKind)
                    ? tab.InternalPageKind
                    : _networkErrorUrls.TryGetValue(view, out var failedUrl) ? failedUrl : view.Source?.ToString() ?? url;
                if (ReferenceEquals(tab.SecondaryView, view)) tab.SecondaryUrl = current; else { tab.FullUrl = current; SyncPinnedSite(tab); }
                if (tab == CurrentTab && ReferenceEquals(tab.ActiveView, view)) UpdateChrome();
            });
            // CanGoBack/CanGoForward are finalized by Chromium after SourceChanged. Previously
            // the chrome was refreshed too early, so the arrows stayed stale until switching
            // tabs caused another UpdateChrome call. HistoryChanged also covers pushState and
            // replaceState navigations used by SPA sites.
            web.HistoryChanged += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(CurrentTab, tab) && ReferenceEquals(tab.ActiveView, view)) UpdateChrome();
            });
            web.DocumentTitleChanged += (_, _) => Dispatcher.Invoke(() =>
            {
                var current = tab.IsInternal && !string.IsNullOrWhiteSpace(tab.InternalPageKind)
                    ? tab.InternalPageKind
                    : _networkErrorUrls.TryGetValue(view, out var failedUrl) ? failedUrl : view.Source?.ToString() ?? url;
                var title = string.IsNullOrWhiteSpace(web.DocumentTitle) ? Domain(current) : web.DocumentTitle;
                if (ReferenceEquals(tab.SecondaryView, view)) tab.SecondaryTitle = title; else tab.Title = title;
                if (!tab.IsInternal && !_networkErrorUrls.ContainsKey(view)) RecordHistory(current, title);
            });
            web.NewWindowRequested += (_, e) =>
            {
                // window.open() with an explicit size is how sign-in flows (Google, Apple, GitHub…)
                // ask for a sheet. Give them a real child window so window.opener keeps working;
                // everything else still becomes a normal tab.
                var features = e.WindowFeatures;
                e.Handled = true;
                var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                var activate = !ctrl;
                var newHost = Uri.TryCreate(e.Uri, UriKind.Absolute, out var newWindowUri) ? newWindowUri.Host : "";
                if (IsConferencingHost(newHost)) { _ = Dispatcher.BeginInvoke(async () => await AddTabAsync(e.Uri, activate)); return; }
                if (!features.HasSize && !features.HasPosition) { _ = Dispatcher.BeginInvoke(async () => await AddTabAsync(e.Uri, activate)); return; }
                var deferral = e.GetDeferral();
                _ = Dispatcher.BeginInvoke(async () =>
                {
                    try { await OpenAuthPopupAsync(e, view); }
                    catch (Exception ex) { App.Log(ex); await AddTabAsync(e.Uri, activate); }
                    finally { deferral.Complete(); }
                });
            };
            web.FaviconChanged += (_, _) => Dispatcher.Invoke(() =>
            {
                var icon = web.FaviconUri;
                if (string.IsNullOrWhiteSpace(icon)) return;
                if (ReferenceEquals(tab.SecondaryView, view)) tab.SecondaryFavicon = icon; else tab.Favicon = icon;
            });
            web.DownloadStarting += (_, e) => Dispatcher.Invoke(() => { _lastDownloadStartedUtc[view] = DateTime.UtcNow; HandleDownloadStarting(e); });
            web.DOMContentLoaded += async (_, _) => { await ApplyAmbientLightAsync(view); await ApplyTranslationAsync(view, false); };
            web.NavigationCompleted += async (_, args) =>
            {
                tab.IsNavigating = false;
                if (_networkErrorUrls.ContainsKey(view) && string.Equals(view.Source?.ToString(), "about:blank", StringComparison.OrdinalIgnoreCase)) return;
                var source = view.Source?.ToString() ?? "";
                var engineErrorPage = source.StartsWith("chrome-error://", StringComparison.OrdinalIgnoreCase)
                    || source.StartsWith("edge-error://", StringComparison.OrdinalIgnoreCase);
                if ((!args.IsSuccess || engineErrorPage) && !tab.IsInternal)
                {
                    // 0) Engine error pages carry no useful status; treat "unknown" ones as transient below.
                    // 1) The navigation was replaced by a newer one (redirect, click, reload):
                    //    its failure is meaningless, the newer navigation decides what to show.
                    if (_latestNavigationIds.TryGetValue(view, out var latestId) && args.NavigationId != latestId) return;
                    // 2) Cancelled by the user/engine, or turned into a file download: not an error.
                    if (args.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
                    if (_lastDownloadStartedUtc.TryGetValue(view, out var downloadAt) && (DateTime.UtcNow - downloadAt).TotalSeconds < 5) return;

                    var failedUrl = _pendingNavigationUrls.TryGetValue(view, out var requestedUrl)
                        ? requestedUrl
                        : ReferenceEquals(tab.SecondaryView, view) ? tab.SecondaryUrl : tab.FullUrl;

                    // 3) Only show custom error overlay for HARD connection errors (blocked host, DNS fail, connection refused, offline)
                    //    Ignore Unknown, OperationCanceled, and minor subresource/HTTP stream errors.
                    if (!IsHardNetworkError(args.WebErrorStatus) && !engineErrorPage) return;

                    // 4) If the page ALREADY rendered content or site error page, do NOT wipe it out!
                    if (await PageHasRenderedContentAsync(view)) return;

                    _transientRetries.Remove(view);
                    if (Uri.TryCreate(failedUrl, UriKind.Absolute, out var failedUri) && failedUri.Scheme is "http" or "https")
                    {
                        _networkErrorUrls[view] = failedUrl;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (ReferenceEquals(tab.SecondaryView, view)) { tab.SecondaryUrl = failedUrl; tab.SecondaryTitle = L("Страница недоступна", "Page unavailable", "Сторінка недоступна"); }
                            else { tab.FullUrl = failedUrl; tab.Title = L("Страница недоступна", "Page unavailable", "Сторінка недоступна"); }
                            UpdateChrome();
                        });
                        web.NavigateToString(BuildNetworkErrorPage(failedUrl, args.WebErrorStatus.ToString()));
                    }
                    return;
                }
                _pendingNavigationUrls.Remove(view);
                if (args.IsSuccess) _transientRetries.Remove(view);
                await ApplyPipAsync(view); await ApplyAmbientLightAsync(view); await ApplyTranslationAsync(view, false); await ApplyDomainPrefsAsync(view);
                if (!args.IsSuccess || tab.IsInternal) return;
                await Dispatcher.InvokeAsync(() =>
                {
                    var current = view.Source?.ToString() ?? url;
                    var title = string.IsNullOrWhiteSpace(web.DocumentTitle) ? Domain(current) : web.DocumentTitle;
                    RecordHistory(current, title);
                    ScheduleNavigationSave();
                });
            };
            view.ZoomFactorChanged += (_, _) => RememberZoom(view);
            web.Navigate(url);
        }
        catch (Exception ex) { App.Log(ex); ShowToast("Ошибка загрузки", ex.Message); }
    }

    private void RetryNetworkPage(BrowserTab tab, WebView2 view)
    {
        if (!_networkErrorUrls.Remove(view, out var target) || view.CoreWebView2 is null) return;
        if (ReferenceEquals(tab.SecondaryView, view)) tab.SecondaryUrl = target; else tab.FullUrl = target;
        view.CoreWebView2.Navigate(target);
        UpdateChrome();
    }

    private string BuildNetworkErrorPage(string failedUrl, string errorCode)
    {
        var title = L("Не удалось открыть страницу", "Couldn't open this page", "Не вдалося відкрити сторінку");
        var hint = L("Проверьте подключение к интернету и адрес сайта, затем попробуйте снова.", "Check your internet connection and the site address, then try again.", "Перевірте підключення до інтернету й адресу сайту, а потім спробуйте ще раз.");
        var retry = L("Попробовать снова", "Try again", "Спробувати ще раз");
        var codeLabel = L("Код ошибки", "Error code", "Код помилки");
        var brandSubtitle = L("Браузер в вашем стиле", "Browsing in your style", "Браузер у вашому стилі");
        var networkStatus = L("Сеть Luma", "Luma Network", "Мережа Luma");
        var safeUrl = System.Net.WebUtility.HtmlEncode(failedUrl);
        var safeCode = System.Net.WebUtility.HtmlEncode(errorCode);
        var accent = CurrentAccentCss();
        var background = ThemeCss("TopBrush", "#101015");
        var panel = ThemeCss("PanelBrush", "#17171d");
        var surface = ThemeCss("SurfaceBrush", "#202126");
        var raised = ThemeCss("SurfaceRaisedBrush", "#292a30");
        var border = ThemeCss("BorderBrush", "#34343d");
        var strong = ThemeCss("BorderStrongBrush", "#474752");
        var secondary = ThemeCss("ChromeSecondary", "#cbc8d0");
        var muted = ThemeCss("ChromeMuted", "#89858f");
        var logo = InternalPages.LumaMarkDataUri();
        var html = """
<!doctype html><html lang="__LANG__"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Luma — __TITLE__</title><style>
*{box-sizing:border-box}html,body{height:100%;margin:0}body{overflow:hidden;background:__BACKGROUND__;color:#f8f7fb;font-family:Inter,'Segoe UI Variable','Segoe UI',sans-serif}.ambient{position:fixed;inset:-25%;pointer-events:none;background:radial-gradient(circle at 28% 34%,__ACCENT__30 0,transparent 28%),radial-gradient(circle at 75% 80%,__ACCENT__16 0,transparent 24%);filter:blur(28px)}.shell{position:relative;display:grid;place-items:center;min-height:100%;padding:36px}.card{width:min(680px,100%);padding:12px;border:1px solid __BORDER__;border-radius:30px;background:color-mix(in srgb,__PANEL__ 88%,transparent);box-shadow:0 34px 100px #0009,0 1px 0 #ffffff0a inset}.inner{padding:32px;border:1px solid __BORDER__;border-radius:22px;background:linear-gradient(145deg,__SURFACE__,__PANEL__)}.brand{display:flex;align-items:center;justify-content:space-between;gap:18px}.brand-left{display:flex;align-items:center;gap:13px}.mark{width:46px;height:46px;border-radius:15px;display:grid;place-items:center;background:linear-gradient(145deg,__ACCENT__,color-mix(in srgb,__ACCENT__ 70%,#000));box-shadow:0 12px 34px __ACCENT__45,0 1px 0 #ffffff30 inset;overflow:hidden}.mark img{width:34px;height:34px;object-fit:contain}.name{font-size:14px;font-weight:750;letter-spacing:.01em}.product{display:block;margin-top:3px;color:__MUTED__;font-size:11px;font-weight:600}.status{display:flex;align-items:center;gap:8px;padding:8px 11px;border:1px solid __BORDER__;border-radius:999px;background:__RAISED__;color:__SECONDARY__;font-size:11px;font-weight:650}.status:before{content:'';width:7px;height:7px;border-radius:50%;background:__ACCENT__;box-shadow:0 0 0 4px __ACCENT__20}h1{margin:38px 0 11px;font-size:32px;line-height:1.15;letter-spacing:-.025em}p{max-width:540px;margin:0;color:__SECONDARY__;font-size:14px;line-height:1.7}.url{display:flex;align-items:center;gap:10px;margin-top:26px;padding:13px 15px;border:1px solid __BORDER__;border-radius:13px;background:__RAISED__;color:__SECONDARY__;font:12px 'Cascadia Mono',Consolas,monospace;white-space:nowrap;overflow:hidden}.url:before{content:'';flex:0 0 auto;width:8px;height:8px;border:2px solid __MUTED__;border-radius:50%}.url span{overflow:hidden;text-overflow:ellipsis}.actions{display:flex;align-items:center;gap:14px;margin-top:22px;flex-wrap:wrap}button{height:44px;padding:0 19px;border:1px solid color-mix(in srgb,__ACCENT__ 72%,white);border-radius:13px;background:__ACCENT__;box-shadow:0 9px 26px __ACCENT__35;color:white;font:700 13px inherit;cursor:pointer;transition:.16s transform,.16s filter}button:hover{filter:brightness(1.08);transform:translateY(-1px)}button:active{transform:translateY(0) scale(.98)}.code{padding:8px 10px;border:1px solid __BORDER__;border-radius:9px;background:__BACKGROUND__;color:__MUTED__;font:10.5px 'Cascadia Mono',Consolas,monospace}@media(max-width:560px){.shell{padding:18px}.inner{padding:24px}.status{display:none}h1{font-size:27px}}
</style></head><body><div class="ambient"></div><div class="shell"><main class="card"><section class="inner"><div class="brand"><div class="brand-left"><div class="mark"><img src="__LOGO__" alt="Luma Browser"></div><div><span class="name">Luma Browser</span><span class="product">__BRAND_SUBTITLE__</span></div></div><div class="status">__NETWORK_STATUS__</div></div><h1>__TITLE__</h1><p>__HINT__</p><div class="url" title="__URL__"><span>__URL__</span></div><div class="actions"><button onclick="chrome.webview.postMessage({kind:'luma-network-retry'})">__RETRY__</button><span class="code">__CODE_LABEL__: __CODE__</span></div></section></main></div></body></html>
""";
        return html
            .Replace("__LANG__", System.Net.WebUtility.HtmlEncode(EffectiveLanguage), StringComparison.Ordinal)
            .Replace("__TITLE__", System.Net.WebUtility.HtmlEncode(title), StringComparison.Ordinal)
            .Replace("__HINT__", System.Net.WebUtility.HtmlEncode(hint), StringComparison.Ordinal)
            .Replace("__URL__", safeUrl, StringComparison.Ordinal)
            .Replace("__RETRY__", System.Net.WebUtility.HtmlEncode(retry), StringComparison.Ordinal)
            .Replace("__CODE_LABEL__", System.Net.WebUtility.HtmlEncode(codeLabel), StringComparison.Ordinal)
            .Replace("__CODE__", safeCode, StringComparison.Ordinal)
            .Replace("__BRAND_SUBTITLE__", System.Net.WebUtility.HtmlEncode(brandSubtitle), StringComparison.Ordinal)
            .Replace("__NETWORK_STATUS__", System.Net.WebUtility.HtmlEncode(networkStatus), StringComparison.Ordinal)
            .Replace("__LOGO__", logo, StringComparison.Ordinal)
            .Replace("__ACCENT__", accent, StringComparison.Ordinal)
            .Replace("__BACKGROUND__", background, StringComparison.Ordinal)
            .Replace("__PANEL__", panel, StringComparison.Ordinal)
            .Replace("__SURFACE__", surface, StringComparison.Ordinal)
            .Replace("__RAISED__", raised, StringComparison.Ordinal)
            .Replace("__BORDER__", border, StringComparison.Ordinal)
            .Replace("__STRONG__", strong, StringComparison.Ordinal)
            .Replace("__SECONDARY__", secondary, StringComparison.Ordinal)
            .Replace("__MUTED__", muted, StringComparison.Ordinal);
    }

    private string ThemeCss(string resourceName, string fallback)
    {
        return Resources[resourceName] is SolidColorBrush brush
            ? $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}"
            : fallback;
    }

    private async Task SyncDocumentAudioStateAsync(BrowserTab tab, WebView2 view)
    {
        var web = view.CoreWebView2;
        if (web is null) return;
        bool playing;
        try { playing = web.IsDocumentPlayingAudio; }
        catch { return; }
        if (!playing)
        {
            await Dispatcher.InvokeAsync(() => UpdateMediaState(tab, false, tab.MediaTitle, tab.MediaArtist, tab.MediaArtwork, tab.MediaPosition, tab.MediaDuration, tab.MediaVolume, tab.MediaIsVideo));
            return;
        }
        try
        {
            var raw = await web.ExecuteScriptAsync("""
(()=>{const m=navigator.mediaSession&&navigator.mediaSession.metadata;const all=[...document.querySelectorAll('audio,video')],e=all.find(x=>!x.paused&&!x.ended)||all[0];const art=m&&m.artwork&&m.artwork.length?m.artwork[m.artwork.length-1].src:'';const og=document.querySelector('meta[property="og:image"],meta[name="twitter:image"]');return {title:(m&&m.title)||document.title||'',artist:(m&&m.artist)||'',artwork:art||(og&&og.content)||'',position:e&&isFinite(e.currentTime)?e.currentTime:0,duration:e&&isFinite(e.duration)?e.duration:0,volume:e?(e.muted?0:e.volume):1,isVideo:!!(e&&e.tagName==='VIDEO')};})()
""");
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            string S(string name) => root.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
            double D(string name, double fallback = 0) => root.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : fallback;
            var isVideo = root.TryGetProperty("isVideo", out var video) && video.ValueKind == JsonValueKind.True;
            await Dispatcher.InvokeAsync(() => UpdateMediaState(tab, true, S("title"), S("artist"), S("artwork"), D("position"), D("duration"), D("volume", 1), isVideo));
        }
        catch (Exception ex) { App.Log(ex); }
    }
}
