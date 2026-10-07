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
using System.Text.RegularExpressions;
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

    // ===================== assistant side panel =====================

    private const double AssistantWidth = 430;
    private WebView2? _assistantView;
    private bool _assistantVisible;
    private bool _assistantReady;
    private CancellationTokenSource? _assistantRun;
    private readonly List<AssistantMessage> _assistantHistory = [];

    private void Assistant_Click(object sender, RoutedEventArgs e) => _ = ToggleAssistantAsync();

    /// <summary>Opens or closes the panel. The column resize is instant on purpose: animating it
    /// would resize the page's native WebView2 window on every frame, which is what made the
    /// sidebar stutter before.</summary>
    private async Task ToggleAssistantAsync(bool? show = null)
    {
        var next = show ?? !_assistantVisible;
        _assistantVisible = next;
        AssistantPanel.Visibility = next ? Visibility.Visible : Visibility.Collapsed;
        AssistantColumn.Width = new GridLength(next ? ResponsiveAssistantWidth() : 0);
        UpdateResponsiveLayout();
        AssistantButton.Foreground = next ? new SolidColorBrush(Color.FromRgb(0xC9, 0xBD, 0xFF)) : new SolidColorBrush(Color.FromRgb(0xD9, 0xD3, 0xE6));
        if (!next)
        {
            _assistantRun?.Cancel();
            return;
        }
        await EnsureAssistantAsync();
        UpdateAssistantContext();
        PostAssistant(new { kind = "focus" });
    }

    private double ResponsiveAssistantWidth()
    {
        if (ActualWidth < 1000) return 300;
        if (ActualWidth < 1180) return 340;
        if (ActualWidth < 1380) return 380;
        return AssistantWidth;
    }

    private void UpdateResponsiveLayout()
    {
        if (AccountSurfaceContent is null || AccountHeroColumn is null || AccountCardColumn is null) return;
        var narrow = SiteShell.ActualWidth > 0 && SiteShell.ActualWidth < 860;
        if (narrow)
        {
            AccountHeroColumn.Width = new GridLength(1, GridUnitType.Star); AccountCardColumn.Width = new GridLength(0);
            Grid.SetColumn(AccountHeroPane, 0); Grid.SetRow(AccountHeroPane, 0);
            Grid.SetColumn(AccountCard, 0); Grid.SetRow(AccountCard, 1);
            AccountHeroPane.Margin = new Thickness(4, 0, 4, 20);
            AccountCard.Width = double.NaN; AccountCard.MaxWidth = 510; AccountCard.HorizontalAlignment = WpfHorizontalAlignment.Stretch;
            AccountSurfaceContent.Margin = new Thickness(20, 72, 20, 32); AccountSurfaceContent.VerticalAlignment = WpfVerticalAlignment.Top;
        }
        else
        {
            AccountHeroColumn.Width = new GridLength(.88, GridUnitType.Star); AccountCardColumn.Width = new GridLength(1.12, GridUnitType.Star);
            Grid.SetColumn(AccountHeroPane, 0); Grid.SetRow(AccountHeroPane, 0);
            Grid.SetColumn(AccountCard, 1); Grid.SetRow(AccountCard, 0);
            AccountHeroPane.Margin = new Thickness(6, 0, 70, 0);
            AccountCard.Width = 510; AccountCard.MaxWidth = double.PositiveInfinity; AccountCard.HorizontalAlignment = WpfHorizontalAlignment.Center;
            AccountSurfaceContent.Margin = new Thickness(46, 36, 46, 46); AccountSurfaceContent.VerticalAlignment = WpfVerticalAlignment.Center;
        }
    }

    private async Task EnsureAssistantAsync()
    {
        if (_assistantView is not null) return;
        try
        {
            var view = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 23, 19, 32) };
            _assistantView = view;
            AssistantHost.Children.Add(view);
            await view.EnsureCoreWebView2Async(await BrowserEnvironmentAsync());
            var core = view.CoreWebView2;
            core.ProcessFailed += (_, e) => { App.Log(new Exception($"Assistant ProcessFailed: {e.ProcessFailedKind}")); };
            AttachWebInterfaceLocalization(view);
            core.Settings.AreDefaultContextMenusEnabled = false;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScripts.Scrollbars(CurrentAccentCss()));
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.WebMessageReceived += (_, e) => ReceiveAssistantMessage(e.WebMessageAsJson);
            // Links inside the panel open as normal tabs instead of hijacking the panel.
            core.NewWindowRequested += (_, e) => { e.Handled = true; var target = e.Uri; Dispatcher.BeginInvoke(async () => await AddTabAsync(target)); };
            core.NavigateToString(InternalPages.Assistant());
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("Не удалось открыть LumaAI", ex.Message, true);
        }
    }

    private void PostAssistant(object payload)
    {
        try
        {
            if (_assistantView?.CoreWebView2 is { } core)
                core.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
        }
        catch { }
    }

    /// <summary>Shows which page the assistant is currently looking at.</summary>
    private async void UpdateAssistantContext()
    {
        if (!_assistantReady) return;
        var tab = CurrentTab;
        var label = tab is null ? "Нет активной вкладки" : Domain(tab.ActiveUrl);
        PostAssistant(new { kind = "context", text = label, meta = "Контекст вкладки подключён", preview = "LumaAI видит текст и изображения активной вкладки." });
        try
        {
            if (tab?.ActiveView?.CoreWebView2 is null) return;
            var expectedId = tab.Id;
            var page = await ReadPageContextAsync();
            if (!_assistantReady || CurrentTab?.Id != expectedId) return;
            var preview = !string.IsNullOrWhiteSpace(page.Selection)
                ? "«" + page.Selection.Replace("\r", " ").Replace("\n", " ")[..Math.Min(page.Selection.Length, 180)] + (page.Selection.Length > 180 ? "…»" : "»")
                : !string.IsNullOrWhiteSpace(page.Text)
                    ? page.Text.Replace("\r", " ").Replace("\n", " ")[..Math.Min(page.Text.Length, 180)] + (page.Text.Length > 180 ? "…" : "")
                    : "LumaAI видит снимок активной вкладки.";
            var meta = !string.IsNullOrWhiteSpace(page.Selection) ? $"{page.Selection.Length} символов выделено" : "Текст и изображения вкладки";
            PostAssistant(new { kind = "context", text = label, meta, preview });
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private void ReceiveAssistantMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var kind = root.TryGetProperty("kind", out var value) ? value.GetString() : "";
            switch (kind)
            {
                case "luma-assistant-ready":
                    _assistantReady = true;
                    PostAssistant(new { kind = "prefs", model = _state.AssistantModel,
                        unlimited = _state.AssistantQuota.Unlimited, limit = Math.Max(1, _state.AssistantQuota.Limit) });
                    PostAssistant(new { kind = "history", conversations = _state.AssistantConversations.Select(item => new
                    {
                        id = item.Id, title = item.Title, turns = item.Turns, html = item.Html, updatedAt = item.UpdatedAt.ToString("o")
                    }).ToArray() });
                    PostAssistant(new { kind = "customCommands", items = _state.AssistantCustomCommands.ToArray() });
                    PostQuota(_state.AssistantQuota);
                    _ = RefreshAssistantQuotaAsync();
                    ApplyTheme(_previewTheme);
                    UpdateAssistantContext();
                    return;
                case "luma-assistant-close":
                    Dispatcher.BeginInvoke(async () => await ToggleAssistantAsync(false));
                    return;
                case "luma-assistant-clear":
                    _assistantRun?.Cancel();
                    _assistantHistory.Clear();
                    return;
                case "luma-assistant-prefs":
                    SaveAssistantPrefs(root);
                    return;
                case "luma-assistant-history-save":
                    SaveAssistantConversations(root);
                    return;
                case "luma-assistant-custom-commands":
                    SaveAssistantCustomCommands(root);
                    return;
                case "luma-assistant-history-delete":
                    if (root.TryGetProperty("id", out value) && value.GetString() is { Length: > 0 } id)
                    {
                        _state.AssistantConversations.RemoveAll(item => item.Id == id);
                        _stateStore.Save();
                    }
                    return;
                case "luma-assistant-ask":
                    SaveAssistantPrefs(root);
                    var text = root.TryGetProperty("text", out value) ? value.GetString() ?? "" : "";
                    var isAgentExplicit = root.TryGetProperty("isAgent", out value) && value.GetBoolean();
                    var imageBase64 = root.TryGetProperty("imageBase64", out value) ? value.GetString() : null;
                    var imageMime = root.TryGetProperty("imageMime", out value) ? value.GetString() : null;
                    var imageName = root.TryGetProperty("imageName", out value) ? value.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(text))
                        Dispatcher.BeginInvoke(async () => await AskAssistantAsync(text, imageBase64, imageMime, imageName, isAgentExplicit));
                    return;
            }
        }
        catch (Exception ex) { App.Log(ex); }
    }


    private void SaveAssistantCustomCommands(JsonElement root)
    {
        if (!root.TryGetProperty("commands", out var list) || list.ValueKind != JsonValueKind.Array) return;
        var commands = new List<string>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var command = (item.GetString() ?? "").Trim();
            if (command.Length is < 1 or > 240 || commands.Contains(command, StringComparer.OrdinalIgnoreCase)) continue;
            commands.Add(command);
            if (commands.Count == 12) break;
        }
        _state.AssistantCustomCommands = commands;
        _stateStore.Save();
        PostAssistant(new { kind = "customCommands", items = commands.ToArray() });
    }

    private void SaveAssistantConversations(JsonElement root)
    {
        if (!root.TryGetProperty("conversations", out var list) || list.ValueKind != JsonValueKind.Array) return;
        var saved = new List<AssistantConversationState>();
        var totalPayload = 0;
        foreach (var item in list.EnumerateArray().Take(20))
        {
            string Text(string name) => item.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
            var id = Text("id"); var title = Text("title").Trim(); var html = Text("html");
            if (id.Length is < 4 or > 80 || title.Length == 0 || html.Length > 90_000 || html.Contains("base64,", StringComparison.OrdinalIgnoreCase)) continue;
            totalPayload += html.Length;
            if (totalPayload > 800_000) break;
            var turns = item.TryGetProperty("turns", out var turnsNode) && turnsNode.TryGetInt32(out var count) ? Math.Clamp(count, 1, 200) : 1;
            var updated = item.TryGetProperty("updatedAt", out var dateNode) && DateTime.TryParse(dateNode.GetString(), out var date) ? date.ToUniversalTime() : _clock.UtcNow;
            saved.Add(new AssistantConversationState { Id = id, Title = title[..Math.Min(title.Length, 120)], Turns = turns, Html = html, UpdatedAt = updated });
        }
        _state.AssistantConversations = saved.OrderByDescending(item => item.UpdatedAt).Take(20).ToList();
        _stateStore.Save();
    }

    private void PostQuota(AssistantQuotaState quota)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (quota.AgentQuotaDate != today)
        {
            quota.AgentQuotaDate = today;
            quota.AgentUsed = 0;
            _stateStore.Save();
        }
        var agentRemaining = quota.Unlimited ? 999 : Math.Max(0, quota.AgentLimit - quota.AgentUsed);

        PostAssistant(new
        {
            kind = "quota",
            limit = Math.Max(1, quota.Limit),
            used = Math.Max(0, quota.Used),
            remaining = Math.Max(0, quota.Remaining),
            unlimited = quota.Unlimited,
            agentLimit = quota.AgentLimit,
            agentUsed = quota.AgentUsed,
            agentRemaining = agentRemaining
        });
    }

    private async Task RefreshAssistantQuotaAsync()
    {
        try
        {
            var token = await _auth.GetAccessTokenAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(token)) return;
            var quota = await AssistantClient.GetQuotaAsync(token, CancellationToken.None);
            if (quota is null) return;
            var prevAgentUsed = _state.AssistantQuota.AgentUsed;
            var prevAgentDate = _state.AssistantQuota.AgentQuotaDate;
            _state.AssistantQuota = new AssistantQuotaState
            {
                Limit = quota.Limit,
                Used = quota.Used,
                Remaining = quota.Remaining,
                Unlimited = quota.Unlimited,
                AgentLimit = 3,
                AgentUsed = prevAgentUsed,
                AgentQuotaDate = prevAgentDate,
                UpdatedAt = _clock.UtcNow
            };
            _stateStore.Save();
            PostQuota(_state.AssistantQuota);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    private void SaveAssistantPrefs(JsonElement root)
    {
        var changed = false;
        if (root.TryGetProperty("model", out var model) && model.GetString() is { Length: > 0 } name && name != _state.AssistantModel)
        {
            _state.AssistantModel = name; changed = true;
        }
        if (changed) Dispatcher.Invoke(_stateStore.Save);
    }

    /// <summary>Pulls the visible text, the current selection and the address out of the tab.</summary>
    private async Task<(string Title, string Url, string Selection, string Text)> ReadPageContextAsync()
    {
        var tab = CurrentTab;
        if (tab?.ActiveView.CoreWebView2 is null) return ("", "", "", "");
        // NOTE: verbatim string on purpose. Escape sequences must reach JavaScript
        // untouched, otherwise the regex literals below break the script.
        const string script = @"(()=>{try{
  const clean=t=>String(t||'').replace(/[\t\u00a0 ]+/g,' ').replace(/\n{3,}/g,'\n\n').trim();
  const sel=clean(String(window.getSelection()||'')).slice(0,6000);
  let t='';
  const body=document.body, docEl=document.documentElement;
  if(body&&body.innerText) t=clean(body.innerText);
  if(t.length<40&&docEl&&docEl.innerText) t=clean(docEl.innerText);
  if(t.length<40){
    const parts=[];
    document.querySelectorAll('h1,h2,h3,h4,p,li,td,th,figcaption,blockquote,article,section,span,a,button,label').forEach(el=>{
      const v=(el.innerText||el.textContent||'').trim();
      if(v&&v.length<3000) parts.push(v);
    });
    t=clean(parts.join('\n'));
  }
  const media=[];
  document.querySelectorAll('img,[role=img],svg[aria-label]').forEach(el=>{
    const v=(el.getAttribute('alt')||el.getAttribute('aria-label')||el.getAttribute('title')||'').trim();
    if(v.length>2) media.push(v);
  });
  return JSON.stringify({
    title:document.title||'',
    url:location.href,
    sel:sel,
    ready:document.readyState,
    media:media.slice(0,40).join(' | ').slice(0,2000),
    text:t.slice(0,48000)
  });
}catch(e){return JSON.stringify({err:String(e&&e.message||e)})}})()";

        // Freshly navigated pages can answer before their body exists, so retry briefly.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var raw = await tab.ActiveView.CoreWebView2.ExecuteScriptAsync(script);
                if (!string.IsNullOrWhiteSpace(raw) && raw != "null")
                {
                    var inner = JsonSerializer.Deserialize<string>(raw) ?? "{}";
                    using var doc = JsonDocument.Parse(inner);
                    string P(string name) => doc.RootElement.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
                    var text = P("text");
                    var media = P("media");
                    if (media.Length > 0) text = text.Length > 0 ? text + "\n\n[Подписи к изображениям] " + media : "[Подписи к изображениям] " + media;
                    if (text.Length > 0 || attempt == 3) return (P("title"), P("url"), P("sel"), text);
                }
            }
            catch (Exception ex) { App.Log(ex); }
            await Task.Delay(260);
        }
        return (tab.DisplayTitle, tab.ActiveUrl, "", "");
    }

    /// <summary>PNG screenshot of the visible part of the tab, base64 encoded for the vision API.</summary>
    private async Task<string?> CaptureTabAsync()
    {
        var tab = CurrentTab;
        if (tab?.ActiveView.CoreWebView2 is null) return null;
        try
        {
            using var buffer = new MemoryStream();
            await tab.ActiveView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, buffer);
            if (buffer.Length == 0) return null;
            if (buffer.Length <= 6_000_000) return Convert.ToBase64String(buffer.ToArray());

            // If PNG is over 6MB (e.g. 4K/high-DPI display), fall back to compressed JPEG
            using var jpegBuffer = new MemoryStream();
            await tab.ActiveView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, jpegBuffer);
            if (jpegBuffer.Length > 0 && jpegBuffer.Length <= 6_000_000)
                return Convert.ToBase64String(jpegBuffer.ToArray());
            return null;
        }
        catch (Exception ex) { App.Log(ex); return null; }
    }

    /// <summary>Opens whatever the assistant asked to open, newest tab last and focused.</summary>
    /// <summary>
    /// Runs inside a site search page: presses the site's own "search" button if it has one,
    /// then returns the first link that belongs to this site and whose text matches the title
    /// we are looking for. Returns an empty string while the page is still loading results.
    /// </summary>
    private const string PickResultScript = """
    (() => { try {
      const q = __LUMA_QUERY__;
      const words = q.toLowerCase().split(/[^0-9a-zа-яё]+/i).filter(w => w.length > 2);
      // The site may still show whatever was typed in it last time, so put our own title in
      // the search box, tell the page about it the way a real keyboard would, and submit.
      try {
        const box = [...document.querySelectorAll('input')].find(i => {
          const t = (i.type || 'text').toLowerCase();
          if (t !== 'text' && t !== 'search' && t !== '') return false;
          if (!i.offsetParent && i.getClientRects().length === 0) return false;
          const hint = ((i.name || '') + ' ' + (i.id || '') + ' ' + (i.placeholder || '') + ' ' + (i.className || '')).toLowerCase();
          return /search|story|поиск|найти/.test(hint) || document.querySelectorAll('input').length === 1;
        });
        if (box && box.value.trim().toLowerCase() !== q.trim().toLowerCase()) {
          const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
          setter.call(box, q);
          box.dispatchEvent(new Event('input', { bubbles: true }));
          box.dispatchEvent(new Event('change', { bubbles: true }));
          box.dispatchEvent(new KeyboardEvent('keyup', { bubbles: true, key: 'Enter', keyCode: 13 }));
          box.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: 'Enter', keyCode: 13 }));
          if (box.form) { try { box.form.submit(); } catch (e) {} }
        }
      } catch (e) {}
      try {
        const btn = [...document.querySelectorAll('button, a, div, span')]
          .find(e => /^(начать поиск|искать|найти|search)$/i.test((e.textContent || '').trim()));
        if (btn) btn.click();
      } catch (e) {}
      const host = location.host;
      const bad = /^\/(search|login|register|signin|signup|catalog|top|random|news|user|profile|tag|tags|genre|genres|about|contacts|dmca|donate)(\/|$)/i;
      const here = location.pathname.replace(/\/+$/, '');
      const found = [];
      for (const a of document.querySelectorAll('a')) {
        let u; try { u = new URL(a.getAttribute('href') || '', location.href); } catch (e) { continue; }
        if (u.host !== host) continue;
        const p = u.pathname.replace(/\/+$/, '');
        if (!p || p === '/' || p === here || bad.test(p) || /(?:^|\/)(?:rss|feed)(?:\.xml)?$/i.test(p) || /\.xml$/i.test(p)) continue;
        const text = (a.textContent || '').trim().toLowerCase();
        if (!text) continue;
        let score = 0;
        for (const w of words) if (text.includes(w)) score++;
        if (score === 0) continue;
        if (/\/(anime|film|movie|serial|series|watch|title|release)/i.test(p)) score += 2;
        if (/^\/\d+-[^/]+\.html$/i.test(p)) score += 3;
        if (/index\.php/i.test(p)) continue;
        if (u.search) score -= 1;
        found.push({ href: u.href, score: score });
      }
      if (!found.length) return '';
      found.sort((a, b) => b.score - a.score);
      return found[0].href;
    } catch (e) { return ''; } })()
    """;

    /// <summary>Waits for the search page to render, then navigates the tab to the best match.</summary>
    private async Task AutoPickResultAsync(BrowserTab tab, string query)
    {
        var script = PickResultScript.Replace("__LUMA_QUERY__", JsonSerializer.Serialize(query));
        for (var attempt = 0; attempt < 16; attempt++)
        {
            await Task.Delay(attempt == 0 ? 900 : 550);
            var view = tab.ActiveView;
            if (view.CoreWebView2 is null) continue;
            string raw;
            try { raw = await view.CoreWebView2.ExecuteScriptAsync(script); }
            catch (Exception ex) { App.Log(ex); return; }
            string? hit = null;
            try { hit = JsonSerializer.Deserialize<string>(raw); } catch { }
            if (string.IsNullOrWhiteSpace(hit)) continue;
            view.CoreWebView2.Navigate(hit);
            return;
        }
    }

    /// <summary>Web search used by the browser agent's <c>web_search</c> action.</summary>
    internal async Task<List<(string Title, string Url, string Snippet)>> AgentWebSearchAsync(string query, CancellationToken token)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(12));
            var run = await RunLocalLumaSearchAsync(query, "all", cts.Token);
            return run.Results
                .Where(r => !string.IsNullOrWhiteSpace(r.url))
                .Select(r => (r.title, r.url, r.snippet))
                .ToList();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return []; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { App.Log(ex); return []; }
    }

    private async Task RunAssistantActionsAsync(string answer, bool allowWebSearchTab = true)
    {
        var targets = AssistantActions.Parse(answer);
        if (targets.Count == 0) return;

        var opened = new List<string>();
        foreach (var target in targets)
        {
            try
            {
                if (!allowWebSearchTab && (target.Kind is "web" or "search"))
                    continue;

                // "open" already carries a real address. Everything else is a title the model
                // picked from its own list: Luma looks the title page up on the catalogue site
                // itself instead of dumping the user on a search results page.
                var url = target.Kind == "open"
                    ? target.Value
                    : await SiteResolver.ResolveAsync(target.Kind, target.Value);

                if (!allowWebSearchTab && SiteResolver.IsSearchPage(url))
                    continue;

                var tab = await AddTabAsync(url);
                // When even the web lookup could not name the title page, the tab lands on the
                // site's own search. Do not leave the user there: drive that page from inside and
                // jump to the first result that actually matches the title.
                if (target.Kind != "open" && SiteResolver.IsSearchPage(url))
                    _ = AutoPickResultAsync(tab, target.Value);
                opened.Add(target.Label.Length > 0 ? target.Label : AssistantActions.PrettyHost(url));
            }
            catch (Exception ex) { App.Log(ex); }
        }

        if (opened.Count == 0) return;
        PostAssistant(new { kind = "opened", items = opened });
        ShowToast(opened.Count == 1 ? "Открыто" : $"Открыто вкладок: {opened.Count}", string.Join(", ", opened));
    }

    private async Task AskAssistantAsync(string question, string? attachmentBase64 = null, string? attachmentMime = null, string? attachmentName = null, bool isAgentExplicit = false)
    {
        _assistantRun?.Cancel();
        var run = new CancellationTokenSource();
        _assistantRun = run;

        var accessToken = await _auth.GetAccessTokenAsync(run.Token);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            PostAssistant(new { kind = "error", text = "Войдите в аккаунт Luma, чтобы пользоваться LumaAI." });
            _assistantRun = null;
            return;
        }

        // Autonomous Browser Agent: handles multi-tab research, price/quality comparison, and auto-carting
        var isAgent = isAgentExplicit || BrowserAgent.BrowserAgentRunner.IsAgentGoal(question);
        if (isAgent)
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (_state.AssistantQuota.AgentQuotaDate != today)
            {
                _state.AssistantQuota.AgentQuotaDate = today;
                _state.AssistantQuota.AgentUsed = 0;
                _stateStore.Save();
            }

            // Check overall daily 15-request quota
            if (!_state.AssistantQuota.Unlimited && _state.AssistantQuota.Remaining <= 0)
            {
                var noQuotaMsg = _state.Language switch
                {
                    "uk" => "Денний ліміт запитів LumaAI вичерпано (15 на день). Квота оновиться завтра.",
                    "en" => "Daily LumaAI request limit reached (15 per day). Quota resets tomorrow.",
                    _ => "Дневной лимит запросов LumaAI исчерпан (15 в день). Квота обновится завтра."
                };
                PostAssistant(new { kind = "error", text = noQuotaMsg });
                _assistantRun = null;
                return;
            }

            // Check agent daily 3-request quota
            if (!_state.AssistantQuota.Unlimited && _state.AssistantQuota.AgentUsed >= _state.AssistantQuota.AgentLimit)
            {
                var agentLimitMsg = _state.Language switch
                {
                    "uk" => "Денний ліміт агента вичерпано (3 з 3 на день). Звичайний асистент LumaAI залишається доступним.",
                    "en" => "Daily agent limit reached (3 of 3 today). Regular LumaAI assistant remains available.",
                    _ => "Дневной лимит агента исчерпан (3 из 3 в день). Обычный ассистент LumaAI остаётся доступен."
                };
                PostAssistant(new { kind = "error", text = agentLimitMsg });
                _assistantRun = null;
                return;
            }

            // Deduct quota: 1 from agent quota AND 1 from general 15-request quota
            if (!_state.AssistantQuota.Unlimited)
            {
                _state.AssistantQuota.AgentUsed++;
                _state.AssistantQuota.Used++;
                _state.AssistantQuota.Remaining = Math.Max(0, _state.AssistantQuota.Remaining - 1);
                _stateStore.Save();
                PostQuota(_state.AssistantQuota);
            }

            try
            {
                var agentLabel = _state.Language switch
                {
                    "uk" => "Агент: увімкнено",
                    "en" => "Agent: active",
                    _ => "Агент: включен"
                };
                PostAssistant(new { kind = "context", text = "Luma Agent", meta = agentLabel });

                var conversation = string.Join("\n", _assistantHistory.TakeLast(6)
                    .Select(m => (m.Role == "user" ? "Пользователь: " : "LumaAI: ") + (m.Text.Length > 400 ? m.Text[..400] + "…" : m.Text)));

                string? effectiveImage = attachmentBase64;
                string? effectiveMime = attachmentMime;
                string? effectiveName = attachmentName;

                if (string.IsNullOrWhiteSpace(effectiveImage) && _state.AssistantScreenshot &&
                    Regex.IsMatch(question, @"\b(как тут|на этой странице|как на экране|этот стиль|эта картинка|это изображение|как здесь)\b", RegexOptions.IgnoreCase))
                {
                    var pageShot = await CaptureTabAsync();
                    if (!string.IsNullOrWhiteSpace(pageShot))
                    {
                        effectiveImage = pageShot;
                        effectiveMime = "image/png";
                        effectiveName = "current-tab.png";
                    }
                }

                var agentAnswer = await BrowserAgent.BrowserAgentRunner.ExecuteAgentTaskAsync(
                    question,
                    this,
                    accessToken,
                    _state.Language,
                    async delta =>
                    {
                        await Dispatcher.InvokeAsync(() => PostAssistant(new { kind = "delta", text = delta }));
                    },
                    run.Token,
                    conversation,
                    quota => Dispatcher.Invoke(() =>
                    {
                        _state.AssistantQuota.Limit = quota.Limit;
                        _state.AssistantQuota.Used = quota.Used;
                        _state.AssistantQuota.Remaining = quota.Remaining;
                        _state.AssistantQuota.Unlimited = quota.Unlimited;
                        _stateStore.Save();
                        PostAssistant(new { kind = "quota", limit = quota.Limit, used = quota.Used, remaining = quota.Remaining, unlimited = quota.Unlimited });
                    }),
                    preferredTier: _state.AssistantModel,
                    attachmentBase64: effectiveImage,
                    attachmentMime: effectiveMime,
                    attachmentName: effectiveName);
                _assistantHistory.Add(new AssistantMessage { Role = "user", Text = question });
                if (!string.IsNullOrWhiteSpace(agentAnswer)) _assistantHistory.Add(new AssistantMessage { Role = "assistant", Text = agentAnswer });
                while (_assistantHistory.Count > 30) _assistantHistory.RemoveAt(0);
                PostAssistant(new { kind = "done" });
            }
            catch (OperationCanceledException) { PostAssistant(new { kind = "done" }); }
            catch (Exception ex)
            {
                App.Log(ex);
                PostAssistant(new { kind = "error", text = ex.Message });
            }
            finally
            {
                if (ReferenceEquals(_assistantRun, run)) _assistantRun = null;
            }
            return;
        }

        var page = await ReadPageContextAsync();
        // Fix: respect the user's AssistantScreenshot preference before capturing.
        var pageImage = _state.AssistantScreenshot ? await CaptureTabAsync() : null;
        var images = new List<AssistantImage>();
        if (!string.IsNullOrWhiteSpace(pageImage))
            images.Add(new AssistantImage { Base64 = pageImage, MimeType = "image/png", Name = "current-tab.png" });
        if (!string.IsNullOrWhiteSpace(attachmentBase64) && attachmentBase64.Length <= 8_000_000)
        {
            var mime = attachmentMime is "image/png" or "image/webp" or "image/gif" ? attachmentMime : "image/jpeg";
            images.Add(new AssistantImage
            {
                Base64 = attachmentBase64,
                MimeType = mime,
                Name = string.IsNullOrWhiteSpace(attachmentName) ? "attachment" : attachmentName[..Math.Min(attachmentName.Length, 120)]
            });
        }

        // Silent Web Grounding: if the question looks like a factual query, do a fast
        // background search and inject the snippets into the system prompt so the AI
        // can answer accurately without the user seeing any search activity.
        string webSnippets = "";
        if (AssistantWebSearch.ShouldSearch(question))
        {
            try
            {
                using var searchCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1400));
                var searchRun = await RunLocalLumaSearchAsync(question, "all", searchCts.Token);
                if (searchRun.Results.Count > 0)
                    webSnippets = string.Join("\n", searchRun.Results.Take(4)
                        .Select(r => $"- {r.title}: {r.snippet}".TrimEnd()));
            }
            catch { /* search is best-effort, never block the answer */ }
        }

        var context = new StringBuilder();
        context.AppendLine("Ты — LumaAI, ассистент, встроенный в браузер Luma. Никогда не называй сторонних поставщиков моделей и не говори, на чём ты работаешь.");
        var langPrompt = _state.Language switch
        {
            "uk" => "Відповідай українською мовою (або мовою, якою звернувся користувач), ясно та по суті.",
            "en" => "Reply in English (or in the language used by the user), clear and to the point.",
            _ => "Отвечай на языке запроса пользователя (по умолчанию по-русски), ясно и по делу."
        };
        context.AppendLine(langPrompt + " Используй структуру и короткие абзацы, когда это улучшает ответ.");
        context.AppendLine("Ты видишь текст, снимок текущей вкладки и прикреплённые пользователем изображения. Распознавай текст, объекты, интерфейсы, документы и смысл изображений.");
        context.AppendLine("Если пользователь приложил изображение, оно важнее общего снимка вкладки. Не говори, что не видишь картинку, если она приложена.");
        context.AppendLine(AssistantChoices.Protocol);
        context.AppendLine(AssistantActions.Protocol);
        if (!string.IsNullOrWhiteSpace(webSnippets))
        {
            context.AppendLine("\n[Актуальные факты из фонового поиска браузера]:");
            context.AppendLine(webSnippets);
            context.AppendLine("Используй эти факты как свои собственные знания. КАТЕГОРИЧЕСКИ не упоминай поиск, интернет или источники — отвечай уверенно и естественно.");
        }
        if (!string.IsNullOrWhiteSpace(page.Title)) context.AppendLine($"\nЗаголовок: {page.Title}");
        if (!string.IsNullOrWhiteSpace(page.Url)) context.AppendLine($"Адрес: {page.Url}");
        if (!string.IsNullOrWhiteSpace(page.Selection)) context.AppendLine($"\nВыделенный фрагмент (главный приоритет):\n{page.Selection}");
        if (!string.IsNullOrWhiteSpace(page.Text)) context.AppendLine($"\nТекст страницы:\n{page.Text}");
        else context.AppendLine("\nТекстового слепка нет — используй снимок вкладки и прикреплённые изображения.");

        _assistantHistory.Add(new AssistantMessage { Role = "user", Text = question, Images = images });
        while (_assistantHistory.Count > 30) _assistantHistory.RemoveAt(0);
        // Keep at most two image-bearing turns in history to avoid repeatedly uploading large data.
        foreach (var old in _assistantHistory.Where(message => message.Images.Count > 0).Reverse().Skip(2)) old.Images.Clear();

        var request = new List<AssistantMessage> { new() { Role = "system", Text = context.ToString() } };
        request.AddRange(_assistantHistory);

        var answer = new StringBuilder();
        try
        {
            var quota = await AssistantClient.StreamAsync(accessToken, _state.AssistantModel, request, async delta =>
            {
                answer.Append(delta);
                await Dispatcher.InvokeAsync(() => PostAssistant(new { kind = "delta", text = delta }));
            }, run.Token);

            var full = answer.ToString();
            var spoken = AssistantActions.StripDirectives(full);
            if (spoken.Length > 0) _assistantHistory.Add(new AssistantMessage { Role = "assistant", Text = spoken });
            var choices = AssistantChoices.Parse(full);
            if (choices.Count > 0)
                PostAssistant(new { kind = "choices", items = choices.Select(choice => new { label = choice.Label, prompt = choice.Prompt }).ToArray() });
            if (quota is not null)
            {
                _state.AssistantQuota.Limit = quota.Limit;
                _state.AssistantQuota.Used = quota.Used;
                _state.AssistantQuota.Remaining = quota.Remaining;
                _state.AssistantQuota.Unlimited = quota.Unlimited;
                _state.AssistantQuota.UpdatedAt = _clock.UtcNow;
                _stateStore.Save();
                PostAssistant(new { kind = "quota", limit = quota.Limit, used = quota.Used,
                    remaining = quota.Remaining, unlimited = quota.Unlimited });
            }
            PostAssistant(new { kind = "done" });
            await RunAssistantActionsAsync(full);
        }
        catch (OperationCanceledException) { PostAssistant(new { kind = "done" }); }
        catch (Exception ex)
        {
            App.Log(ex);
            if (_assistantHistory.Count > 0 && _assistantHistory[^1].Role == "user") _assistantHistory.RemoveAt(_assistantHistory.Count - 1);
            PostAssistant(new { kind = "error", text = ex.Message });
        }
        finally { if (ReferenceEquals(_assistantRun, run)) _assistantRun = null; }
    }

    private readonly List<AssistantMessage> _voiceAssistantHistory = [];

    public async Task AskAssistantDirectAsync(string question, Action<string> onDelta, CancellationToken token, string? screenImageBase64 = null)
    {
        var accessToken = await _auth.GetAccessTokenAsync(token);
        var tokenAuth = !string.IsNullOrWhiteSpace(accessToken) ? accessToken : Authentication.SupabaseOptions.PublishableKey;

        // If screen capture was not provided explicitly, capture current screen automatically
        if (string.IsNullOrWhiteSpace(screenImageBase64))
        {
            try
            {
                screenImageBase64 = Voice.DesktopCapture.CaptureScreenBase64();
            }
            catch { }
        }

        var images = new List<AssistantImage>();
        if (!string.IsNullOrWhiteSpace(screenImageBase64))
        {
            images.Add(new AssistantImage
            {
                Base64 = screenImageBase64,
                MimeType = "image/jpeg",
                Name = "screen.jpg"
            });
        }

        // Silent Web Grounding (like Gemini): perform quick background search for the question
        // without opening any browser tabs or search engine pages.
        string webSnippets = "";
        try
        {
            using var searchCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            searchCts.CancelAfter(TimeSpan.FromMilliseconds(2200));
            var searchRun = await RunLocalLumaSearchAsync(question, "all", searchCts.Token);
            if (searchRun.Results.Count > 0)
            {
                webSnippets = string.Join("\n\n", searchRun.Results.Take(5)
                    .Select(r => $"[{r.title}]: {r.snippet}".TrimEnd()));
            }
        }
        catch { /* best-effort silent web search */ }

        var context = new StringBuilder();
        context.AppendLine("Ты — LumaAI, умный голосовой ассистент браузера Luma с компьютерным зрением (работаешь как Gemini Live / Project Astra).");
        context.AppendLine("ТЕБЕ ПЕРЕДАН АКТУАЛЬНЫЙ СНИМОК ЭКРАНА ПОЛЬЗОВАТЕЛЯ в момент вопроса (игра, приложение, окно, рабочий стол, сайт, видео или документ). Курсор мыши пользователя отображен на снимке.");
        context.AppendLine("Ты ВИДИШЬ экран пользователя и всё, что на нём отображается: игры, интерфейсы, персонажей, текст, ошибки, кнопки, предметы, здоровье, графику.");
        context.AppendLine("Когда пользователь спрашивает: «что это такое?», «что на экране?», «помоги пройти», «что это за предмет/персонаж», «переведи это», «кто это?», «что делать дальше?» — внимательно изучи снимок экрана и дай точный, понятный и полезный ответ.");
        context.AppendLine("Отвечай кратко, емко, естественно, дружелюбно и по делу приятным разговорным языком без лишней 'воды'.");
        context.AppendLine("ГЛАВНОЕ ПРАВИЛО: Ты сам находишь актуальную информацию в интернете. НИКОГДА не открывай поисковики, расширенный поиск или страницы поиска во вкладках браузера. Отвечай на вопросы пользователя прямо здесь своим голосом и текстом.");
        context.AppendLine("Если пользователь просит именно открыть конкретный сайт или запустить плеер («открой ютуб», «включи песню...»), только тогда используй протокол действий:");
        context.AppendLine(AssistantActions.Protocol);

        if (!string.IsNullOrWhiteSpace(webSnippets))
        {
            context.AppendLine("\n[Свежие факты из фонового поиска интернета]:");
            context.AppendLine(webSnippets);
            context.AppendLine("Используй эти актуальные факты для точного ответа. Отвечай уверенно, как своими собственными знаниями, без фраз 'я поискал в интернете'.");
        }

        // Maintain conversation context of up to 20 messages for Voice Luma
        _voiceAssistantHistory.Add(new AssistantMessage { Role = "user", Text = question, Images = images });
        while (_voiceAssistantHistory.Count > 20) _voiceAssistantHistory.RemoveAt(0);

        // Keep images only on the most recent 1-2 turns to save bandwidth and token limits while preserving text history
        foreach (var old in _voiceAssistantHistory.Where(m => m.Images.Count > 0).Reverse().Skip(1))
        {
            old.Images.Clear();
        }

        var request = new List<AssistantMessage>
        {
            new() { Role = "system", Text = context.ToString() }
        };
        request.AddRange(_voiceAssistantHistory);

        var answer = new StringBuilder();
        try
        {
            await AssistantClient.StreamAsync(tokenAuth, _state.AssistantModel, request, async delta =>
            {
                answer.Append(delta);
                onDelta(delta);
                await Task.CompletedTask;
            }, token);

            var full = answer.ToString();
            var spoken = AssistantActions.StripDirectives(full);
            if (spoken.Length > 0)
            {
                _voiceAssistantHistory.Add(new AssistantMessage { Role = "assistant", Text = spoken });
                while (_voiceAssistantHistory.Count > 20) _voiceAssistantHistory.RemoveAt(0);
            }

            // Only allow opening actual sites/media if the model requested it, NEVER opening search engine pages
            await RunAssistantActionsAsync(full, allowWebSearchTab: false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.Log(ex);
            onDelta("Ошибка: " + ex.Message);
        }
    }
}
