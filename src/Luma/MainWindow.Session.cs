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
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    internal static void TrimProcessMemory()
    {
        try
        {
            GC.Collect(1, GCCollectionMode.Optimized, false);
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);

            var currentSessionId = Process.GetCurrentProcess().SessionId;
            foreach (var proc in Process.GetProcessesByName("msedgewebview2"))
            {
                try
                {
                    if (proc.SessionId == currentSessionId)
                    {
                        EmptyWorkingSet(proc.Handle);
                    }
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch { }
    }

    private void SleepWatchTick()
    {
        if (_state.SleepAfterMinutes == 0) return;
        var limit = TimeSpan.FromMinutes(Math.Max(1, _state.SleepAfterMinutes));
        var suspendedAny = false;
        foreach (var space in _spaces)
            foreach (var tab in space.Tabs.ToList())
            {
                if (tab == CurrentTab || tab.IsAsleep || tab.IsInternal || tab.IsSplit || tab.IsNavigating) continue;
                if (_clock.UtcNow - tab.LastActiveAt < limit) continue;
                var core = tab.View.CoreWebView2;
                if (core is null) continue;
                // A tab actually making sound (music/video playing in the background) stays
                // awake regardless of how long it has been out of sight.
                try { if (core.IsDocumentPlayingAudio) { tab.LastActiveAt = _clock.UtcNow; continue; } }
                catch (Exception ex) { App.Log(ex); }
                try
                {
                    core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                    if (tab.SecondaryView?.CoreWebView2 is { } secCore)
                        secCore.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;

                    tab.IsAsleep = true;
                    _ = core.TrySuspendAsync();
                    _ = tab.SecondaryView?.CoreWebView2?.TrySuspendAsync();
                    suspendedAny = true;
                }
                catch (Exception ex) { tab.IsAsleep = false; App.Log(ex); }
            }

        if (suspendedAny)
        {
            TrimProcessMemory();
        }
    }

    private void WakeTab(BrowserTab tab)
    {
        tab.LastActiveAt = _clock.UtcNow;
        try
        {
            if (tab.View.CoreWebView2 is { } core)
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            if (tab.SecondaryView?.CoreWebView2 is { } secCore)
                secCore.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
        }
        catch (Exception ex) { App.Log(ex); }

        if (!tab.IsAsleep) return;
        tab.IsAsleep = false;
        try { tab.View.CoreWebView2?.Resume(); tab.SecondaryView?.CoreWebView2?.Resume(); }
        catch (Exception ex) { App.Log(ex); }
    }

    private static string DomainKey(string url) => UrlTools.DomainKey(url);

    /// <summary>Re-applies the per-site zoom and dark palette after every navigation.</summary>
    private async Task ApplyDomainPrefsAsync(WebView2 view)
    {
        var domain = DomainKey(view.Source?.ToString() ?? "");
        if (domain.Length == 0) return;
        try
        {
            var zoom = _state.ZoomByDomain.TryGetValue(domain, out var saved) ? saved : Math.Clamp(_state.PageScale / 100d, .8, 2d);
            if (Math.Abs(view.ZoomFactor - zoom) > .01) view.ZoomFactor = zoom;
            if (_state.ForceDarkDomains.Contains(domain)) await Script(view, BrowserScripts.DarkOn);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    private void RememberZoom(WebView2 view)
    {
        if (((App)Application.Current).IsPrivateSession) return;
        var domain = DomainKey(view.Source?.ToString() ?? "");
        if (domain.Length == 0) return;
        var zoom = Math.Round(view.ZoomFactor, 2);
        var defaultZoom = Math.Clamp(_state.PageScale / 100d, .8, 2d);
        if (Math.Abs(zoom - defaultZoom) < .01) { if (!_state.ZoomByDomain.Remove(domain)) return; }
        else if (_state.ZoomByDomain.TryGetValue(domain, out var old) && Math.Abs(old - zoom) < .01) return;
        else _state.ZoomByDomain[domain] = zoom;
        ScheduleNavigationSave();
    }

    private async Task ToggleSiteDarkAsync(WebView2? view)
    {
        if (view?.CoreWebView2 is null) return;
        var domain = DomainKey(view.Source?.ToString() ?? "");
        if (domain.Length == 0) return;
        if (_state.ForceDarkDomains.Remove(domain)) { await Script(view, BrowserScripts.DarkOff); ShowToast("\u0422\u0451\u043c\u043d\u044b\u0439 \u0440\u0435\u0436\u0438\u043c \u0432\u044b\u043a\u043b\u044e\u0447\u0435\u043d", domain); }
        else { _state.ForceDarkDomains.Add(domain); await Script(view, BrowserScripts.DarkOn); ShowToast("\u0422\u0451\u043c\u043d\u044b\u0439 \u0440\u0435\u0436\u0438\u043c \u0432\u043a\u043b\u044e\u0447\u0451\u043d", domain); }
        _stateStore.Save();
    }

    private void ToggleForgetOnExit(WebView2? view)
    {
        if (((App)Application.Current).IsPrivateSession) return;
        var domain = DomainKey(view?.Source?.ToString() ?? "");
        if (domain.Length == 0) return;
        if (_state.ForgetOnExitDomains.Remove(domain)) ShowToast("\u0414\u0430\u043d\u043d\u044b\u0435 \u0441\u043e\u0445\u0440\u0430\u043d\u044f\u044e\u0442\u0441\u044f", domain);
        else { _state.ForgetOnExitDomains.Add(domain); ShowToast("\u0411\u0443\u0434\u0435\u0442 \u0437\u0430\u0431\u044b\u0442 \u043f\u0440\u0438 \u0432\u044b\u0445\u043e\u0434\u0435", domain); }
        _stateStore.Save();
    }

    /// <summary>Wipes the sites marked "forget on exit" during the next start, quietly.</summary>
    private async Task ClearForgetDomainsAsync()
    {
        if (_state.ForgetOnExitDomains.Count == 0) return;
        var core = CurrentTab?.View.CoreWebView2;
        if (core is null) return;
        foreach (var domain in _state.ForgetOnExitDomains.ToList())
            foreach (var origin in new[] { "https://" + domain, "https://www." + domain, "http://" + domain })
            {
                try { await core.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin", JsonSerializer.Serialize(new { origin, storageTypes = "all" })); }
                catch (Exception ex) { App.Log(ex); }
            }
    }

    private void RememberClosed(BrowserTab tab)
    {
        if (((App)Application.Current).IsPrivateSession) return;
        var url = tab.FullUrl;
        if (tab.IsInternal || string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
        _state.RecentlyClosed.RemoveAll(entry => string.Equals(entry.Url, url, StringComparison.OrdinalIgnoreCase));
        _state.RecentlyClosed.Insert(0, new HistoryEntry { Url = url, Title = tab.Title });
        if (_state.RecentlyClosed.Count > 25) _state.RecentlyClosed.RemoveRange(25, _state.RecentlyClosed.Count - 25);
    }

    private async Task ReopenLastClosedAsync()
    {
        var entry = _state.RecentlyClosed.FirstOrDefault();
        if (entry is null) { ShowToast("\u041d\u0435\u0442 \u043d\u0435\u0434\u0430\u0432\u043d\u043e \u0437\u0430\u043a\u0440\u044b\u0442\u044b\u0445"); return; }
        _state.RecentlyClosed.RemoveAt(0); _stateStore.Save();
        await AddTabAsync(entry.Url);
    }

    /// <summary>Strips tracking tails (utm_*, fbclid, …) from the clipboard link before pasting.</summary>
    private async Task PasteCleanAsync()
    {
        try
        {
            var text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            if (string.IsNullOrWhiteSpace(text)) return;
            var cleaned = CleanLink(text);
            Clipboard.SetText(cleaned);
            var view = CurrentTab?.ActiveView;
            if (view?.CoreWebView2 is not null)
            {
                var payload = JsonSerializer.Serialize(cleaned);
                await Script(view, "(()=>{try{const t=" + payload + ";const el=document.activeElement;if(el&&'value' in el&&typeof el.value==='string'){const s=el.selectionStart??el.value.length,f=el.selectionEnd??s;el.value=el.value.slice(0,s)+t+el.value.slice(f);el.selectionStart=el.selectionEnd=s+t.length;el.dispatchEvent(new Event('input',{bubbles:true}))}else document.execCommand('insertText',false,t)}catch(e){}})()");
            }
            ShowToast("\u0421\u0441\u044b\u043b\u043a\u0430 \u0431\u0435\u0437 \u0442\u0440\u0435\u043a\u0435\u0440\u043e\u0432", cleaned.Length > 64 ? cleaned[..64] + "\u2026" : cleaned);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    internal static string CleanLink(string text) => UrlTools.CleanTrackingLink(text);

    private void ScheduleNavigationSave()
    {
        _navigationSaveDebounce?.Cancel();
        var pending = _navigationSaveDebounce = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(700, pending.Token);
                await Dispatcher.InvokeAsync(() => { if (!pending.IsCancellationRequested && !_closing) Save(); });
            }
            catch (OperationCanceledException) { }
            finally { if (ReferenceEquals(_navigationSaveDebounce, pending)) _navigationSaveDebounce = null; pending.Dispose(); }
        });
    }

    private void RecordHistory(string url, string title)
    {
        if (((App)Application.Current).IsPrivateSession) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        var existing = _state.History.FirstOrDefault(h => string.Equals(h.Url, url, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) _state.History.Remove(existing);
        var entry = existing ?? new HistoryEntry { Url = url };
        entry.Title = string.IsNullOrWhiteSpace(title) ? uri.Host : title;
        entry.VisitedAt = _clock.UtcNow;
        _state.History.Insert(0, entry);
        if (_state.History.Count > 5000) _state.History.RemoveRange(5000, _state.History.Count - 5000);
        ScheduleNavigationSave();
    }
    internal void CloseTab(BrowserTab tab)
    {
        var owner = _spaces.FirstOrDefault(space => space.Tabs.Contains(tab));
        if (owner is null) return;

        ReleaseTabInputCapture();
        ++_tabActivationSerial; // cancel every delayed activation/focus pass targeting old rows
        RememberClosed(tab);
        var index = owner.Tabs.IndexOf(tab);
        var wasWindowCurrent = ReferenceEquals(tab, CurrentTab);
        var wasSpaceCurrent = ReferenceEquals(tab, owner.Current);

        owner.Tabs.Remove(tab);
        BrowserTab? replacement = null;
        if (wasSpaceCurrent || wasWindowCurrent)
        {
            replacement = owner.Tabs.Count == 0 && ReferenceEquals(owner, _spaces[_activeSpace])
                ? EnsureHomeTab(owner)
                : owner.Tabs.Count == 0 ? null : owner.Tabs[Math.Clamp(index - 1, 0, owner.Tabs.Count - 1)];
            owner.Current = replacement;
        }

        // Switch layout and ListBox selection before disposing the old native WebView window.
        // Disposing first could leave PaneHost/focus pointing at a dead child HWND.
        if (wasWindowCurrent) CurrentTab = replacement;
        FilterTabs();
        NormalizeTabSelection();
        ForgetViewState(tab.View); ForgetViewState(tab.SecondaryView);
        tab.Dispose();
        TrimProcessMemory();

        if (ReferenceEquals(_floatingVideoTab, tab))
        {
            _ = StopFloatingVideoAsync(true);
        }
        if (ReferenceEquals(_nowPlaying, tab))
        {
            _nowPlaying = null;
            RefreshNowPlaying();
            RefreshFloatingMusic();
        }
        Save();
    }

    private void NormalizeTabSelection()
    {
        if (TabsList is null) return;
        var current = CurrentTab;
        var visibleCurrent = current is not null
            && Tabs.Contains(current)
            && string.IsNullOrEmpty(current.FolderId)
            && !current.IsPinned;
        _syncingTabSelection = true;
        try
        {
            TabsList.SelectedItem = visibleCurrent ? current : null;
            if (visibleCurrent) TabsList.ScrollIntoView(current);
        }
        finally { _syncingTabSelection = false; }
        UpdateChrome();
        RenderPanes();
    }
    private void ClearTabs()
    {
        var targets = Tabs.Where(t => string.IsNullOrEmpty(t.FolderId)).ToList();
        if (targets.Count == 0) { ShowToast("Нет временных вкладок"); return; }
        if (_state.ConfirmManyTabs && targets.Count > 1) { Confirm("Закрыть временные вкладки?", $"Будет закрыто вкладок: {targets.Count}. Папки и закреплённые сайты сохранятся.", ClearTabsCore); return; }
        ClearTabsCore();
    }
    private void ClearTabsCore()
    {
        foreach (var tab in Tabs.Where(t => string.IsNullOrEmpty(t.FolderId)).ToList()) CloseTab(tab);
        FilterTabs(); ShowToast("Вкладки очищены", "Папки сохранены");
    }

    private void ShowTabMenu(BrowserTab tab)
    {
        var menu = NewMenu();
        Add(menu, "Закрепить", () => PinTab(tab));
        Add(menu, "Удалить", () => { CloseTab(tab); FilterTabs(); });
        OpenMenu(menu);
    }

    /// <summary>Moves a live tab into the sidebar PINNED section for good: it leaves the TABS
    /// list, keeps running, and comes back only when the user unpins it.</summary>
    private void PinTab(BrowserTab tab)
    {
        var space = _spaces[_activeSpace];
        var site = space.State.LooseItems.FirstOrDefault(x => string.Equals(x.Url, tab.ActiveUrl, StringComparison.OrdinalIgnoreCase));
        if (site is null) { site = new SavedSite { Title = tab.DisplayTitle, Url = tab.ActiveUrl }; space.State.LooseItems.Add(site); }
        site.TabId = tab.Id; tab.IsPinned = true; tab.FolderId = null;
        Save(); RefreshFolders(); FilterTabs();
        if (CurrentTab == tab) UpdateChrome();
    }

    /// <summary>Finds the tab that already belongs to a sidebar row, if there is one.</summary>
    private BrowserTab? ResolveSiteTab(SavedSite site, FolderState? folder)
        => (!string.IsNullOrEmpty(site.TabId) ? Tabs.FirstOrDefault(t => t.Id == site.TabId) : null)
            ?? Tabs.FirstOrDefault(t => string.Equals(t.FullUrl, site.Url, StringComparison.OrdinalIgnoreCase) || string.Equals(t.SecondaryUrl, site.Url, StringComparison.OrdinalIgnoreCase))
            ?? (folder is null ? Tabs.FirstOrDefault(t => t.IsPinned && SameSite(t.FullUrl, site.Url)) : null);

    /// <summary>True when two URLs belong to the same site, so a pinned row still finds its
    /// tab after the page navigated somewhere deeper.</summary>
    private static bool SameSite(string a, string b) => UrlTools.SameSite(a, b);

    /// <summary>Keeps a pinned sidebar row pointing at wherever its tab navigated.</summary>
    private void SyncPinnedSite(BrowserTab tab)
    {
        if (!tab.IsPinned || _spaces.Count == 0) return;
        var site = _spaces[_activeSpace].State.LooseItems.FirstOrDefault(s => s.TabId == tab.Id);
        if (site is null) return;
        // Never rewrite the pinned address: the row must keep pointing at the site it was
        // pinned to, even while its tab browses deeper pages of that same site.
        if (SameSite(tab.FullUrl, site.Url) && !string.IsNullOrWhiteSpace(tab.Title)) site.Title = tab.Title;
    }

    private void UnpinSavedSite(SavedSite site)
    {
        var space = _spaces[_activeSpace];
        var tab = (!string.IsNullOrEmpty(site.TabId) ? space.Tabs.FirstOrDefault(t => t.Id == site.TabId) : null)
            ?? space.Tabs.FirstOrDefault(t => string.Equals(t.FullUrl, site.Url, StringComparison.OrdinalIgnoreCase));
        space.State.LooseItems.Remove(site);
        if (tab is not null) tab.IsPinned = false;
        Save(); RefreshFolders(); FilterTabs();
        if (tab is not null) CurrentTab = tab;
    }

    // ===================== тихие действия страницы =====================

    /// <summary>Opens the assistant panel and asks it something on the user's behalf.
    /// The panel renders the question exactly like a typed one, so there is no second UI.</summary>
}
