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
using System.Net.Http.Headers;
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

    private bool IsOwnerAccount => _auth.CurrentUser?.Email?.Equals("dakowerr@gmail.com", StringComparison.OrdinalIgnoreCase) == true;
    private bool IsAssistantUnlimited => _state.AssistantQuota.Unlimited || IsOwnerAccount;

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
                    if (IsOwnerAccount) _state.AssistantQuota.Unlimited = true;
                    PostAssistant(new { kind = "prefs", model = _state.AssistantModel,
                        unlimited = IsAssistantUnlimited, limit = Math.Max(1, _state.AssistantQuota.Limit) });
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
                case "luma-image-copy":
                    if (root.TryGetProperty("url", out value) && value.GetString() is { Length: > 0 } copyUrl)
                    {
                        Dispatcher.BeginInvoke(() => CopyImageToClipboard(copyUrl));
                    }
                    return;
                case "luma-image-download":
                    if (root.TryGetProperty("url", out value) && value.GetString() is { Length: > 0 } downloadUrl)
                    {
                        var name = root.TryGetProperty("name", out var nVal) ? nVal.GetString() : "luma_image";
                        Dispatcher.BeginInvoke(async () => await DownloadImageAsync(downloadUrl, name));
                    }
                    return;
                case "luma-image-open-tab":
                    if (root.TryGetProperty("url", out value) && value.GetString() is { Length: > 0 } openUrl)
                    {
                        Dispatcher.BeginInvoke(() => OpenImageInNewTab(openUrl));
                    }
                    return;
                case "luma-assistant-ask":
                    SaveAssistantPrefs(root);
                    var text = root.TryGetProperty("text", out value) ? value.GetString() ?? "" : "";
                    var isAgentExplicit = root.TryGetProperty("isAgent", out value) && value.GetBoolean();
                    var imageBase64 = root.TryGetProperty("imageBase64", out value) ? value.GetString() : null;
                    var imageMime = root.TryGetProperty("imageMime", out value) ? value.GetString() : null;
                    var imageName = root.TryGetProperty("imageName", out value) ? value.GetString() : null;

                    var attachments = new List<AssistantAttachment>();
                    if (root.TryGetProperty("attachments", out var attsProp) && attsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in attsProp.EnumerateArray())
                        {
                            var b64 = item.TryGetProperty("base64", out var bVal) ? bVal.GetString() : null;
                            if (!string.IsNullOrWhiteSpace(b64))
                            {
                                var m = item.TryGetProperty("mime", out var mVal) ? mVal.GetString() ?? "image/jpeg" : "image/jpeg";
                                var n = item.TryGetProperty("name", out var nVal) ? nVal.GetString() ?? "image" : "image";
                                var isV = item.TryGetProperty("isVideo", out var vVal) && vVal.GetBoolean();
                                attachments.Add(new AssistantAttachment { Base64 = b64, Mime = m, Name = n, IsVideo = isV });
                            }
                        }
                    }
                    if (attachments.Count == 0 && !string.IsNullOrWhiteSpace(imageBase64))
                    {
                        attachments.Add(new AssistantAttachment { Base64 = imageBase64, Mime = imageMime ?? "image/jpeg", Name = imageName ?? "image" });
                    }

                    if (!string.IsNullOrWhiteSpace(text))
                        Dispatcher.BeginInvoke(async () => await AskAssistantAsync(text, attachments, isAgentExplicit));
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
        if (IsOwnerAccount) quota.Unlimited = true;
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (quota.AgentQuotaDate != today)
        {
            quota.AgentQuotaDate = today;
            quota.AgentUsed = 0;
            _stateStore.Save();
        }
        if (quota.ImageQuotaDate != today)
        {
            quota.ImageQuotaDate = today;
            quota.ImageUsed = 0;
            _stateStore.Save();
        }
        var isUnlimited = quota.Unlimited || IsOwnerAccount;
        var agentRemaining = isUnlimited ? 999 : Math.Max(0, quota.AgentLimit - quota.AgentUsed);
        var imageRemaining = isUnlimited ? 999 : Math.Max(0, quota.ImageLimit - quota.ImageUsed);

        PostAssistant(new
        {
            kind = "quota",
            limit = Math.Max(1, quota.Limit),
            used = Math.Max(0, quota.Used),
            remaining = isUnlimited ? 999 : Math.Max(0, quota.Remaining),
            unlimited = isUnlimited,
            agentLimit = quota.AgentLimit,
            agentUsed = quota.AgentUsed,
            agentRemaining = agentRemaining,
            imageLimit = quota.ImageLimit,
            imageUsed = quota.ImageUsed,
            imageRemaining = imageRemaining
        });
    }

    private async Task RefreshAssistantQuotaAsync()
    {
        try
        {
            var isOwner = IsOwnerAccount;
            var token = await _auth.GetAccessTokenAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(token))
            {
                if (isOwner)
                {
                    _state.AssistantQuota.Unlimited = true;
                    PostQuota(_state.AssistantQuota);
                }
                return;
            }
            var quota = await AssistantClient.GetQuotaAsync(token, CancellationToken.None);
            if (quota is null)
            {
                if (isOwner)
                {
                    _state.AssistantQuota.Unlimited = true;
                    PostQuota(_state.AssistantQuota);
                }
                return;
            }
            var prevAgentUsed = _state.AssistantQuota.AgentUsed;
            var prevAgentDate = _state.AssistantQuota.AgentQuotaDate;
            var prevImageUsed = _state.AssistantQuota.ImageUsed;
            var prevImageDate = _state.AssistantQuota.ImageQuotaDate;
            _state.AssistantQuota = new AssistantQuotaState
            {
                Limit = quota.Limit,
                Used = quota.Used,
                Remaining = isOwner || quota.Unlimited ? 999 : quota.Remaining,
                Unlimited = quota.Unlimited || isOwner,
                AgentLimit = 3,
                AgentUsed = prevAgentUsed,
                AgentQuotaDate = prevAgentDate,
                ImageLimit = 2,
                ImageUsed = prevImageUsed,
                ImageQuotaDate = prevImageDate,
                UpdatedAt = _clock.UtcNow
            };
            _stateStore.Save();
            PostQuota(_state.AssistantQuota);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            if (IsOwnerAccount)
            {
                _state.AssistantQuota.Unlimited = true;
                PostQuota(_state.AssistantQuota);
            }
        }
    }

    private void CopyImageToClipboard(string url)
    {
        try
        {
            byte[] bytes;
            if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var comma = url.IndexOf(',');
                if (comma < 0) return;
                bytes = Convert.FromBase64String(url[(comma + 1)..]);
            }
            else
            {
                using var http = new HttpClient();
                bytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
            }

            using var ms = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();

            Clipboard.SetImage(image);
            ShowToast("Скопировано", "Изображение скопировано в буфер обмена");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("Ошибка копирования", ex.Message, true);
        }
    }

    private async Task DownloadImageAsync(string url, string? suggestedName)
    {
        try
        {
            var ext = url.Contains("image/png") ? ".png" : (url.Contains("image/webp") ? ".webp" : ".jpg");
            var cleanName = "luma_image";
            if (!string.IsNullOrWhiteSpace(suggestedName))
            {
                var invalids = Path.GetInvalidFileNameChars();
                var cleaned = new string(suggestedName.Where(c => !invalids.Contains(c)).ToArray()).Trim();
                if (!string.IsNullOrWhiteSpace(cleaned))
                    cleanName = cleaned.Length > 40 ? cleaned[..40] : cleaned;
            }

            var dlg = new SaveFileDialog
            {
                FileName = cleanName + ext,
                Filter = "Изображение (*" + ext + ")|*" + ext + "|Все файлы (*.*)|*.*",
                DefaultExt = ext
            };

            if (dlg.ShowDialog(this) != true) return;

            byte[] bytes;
            if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var comma = url.IndexOf(',');
                if (comma < 0) return;
                bytes = Convert.FromBase64String(url[(comma + 1)..]);
            }
            else
            {
                using var http = new HttpClient();
                bytes = await http.GetByteArrayAsync(url);
            }

            await File.WriteAllBytesAsync(dlg.FileName, bytes);
            ShowToast("Изображение сохранено", Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("Ошибка сохранения", ex.Message, true);
        }
    }

    private void OpenImageInNewTab(string url)
    {
        try
        {
            if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var tempHtml = Path.Combine(Path.GetTempPath(), $"luma_img_{Guid.NewGuid():N}.html");
                File.WriteAllText(tempHtml, $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Luma AI Image</title><style>html,body{{margin:0;padding:0;background:#0d0e12;display:grid;place-items:center;min-height:100vh;overflow:auto}}img{{max-width:96vw;max-height:96vh;object-fit:contain;border-radius:10px;box-shadow:0 16px 64px rgba(0,0,0,0.85)}}</style></head><body><img src=\"{url}\"></body></html>");
                _ = AddTabAsync("file:///" + tempHtml.Replace('\\', '/'));
            }
            else
            {
                _ = AddTabAsync(url);
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
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
    internal async Task AutoPickResultAsync(BrowserTab tab, string query)
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
        BringToFrontAndActivate();
    }

    private static readonly HttpClient ImageGenHttp = new(new SocketsHttpHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(15)
    }) { Timeout = TimeSpan.FromMinutes(2) };

    private static bool IsImageGenerationRequest(string text, int imageCount)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lower = text.Trim().ToLowerInvariant();

        // 1. Negative filters: code, text, lists, tables, translations, system tasks
        if (Regex.IsMatch(lower, @"\b(код|скрипт|программ\w*|таблиц\w*|список|списк\w*|переведи|перевод|текст\w*|стать\w*|эссе|сочинени\w*|парол\w*|число|числа|рецепт\w*|план\w*|задач\w*|реши|sql|html|css|json|api|функци\w*|класс\w*)\b", RegexOptions.IgnoreCase))
            return false;

        // 2. Direct visual nouns
        var hasVisualNoun = Regex.IsMatch(lower, @"\b(картинк\w*|изображен\w*|фотк\w*|фото\w*|фотографи\w*|арт\w*|иллюстрац\w*|рисунок|рисунк\w*|пикч\w*|обои|постер|аватар\w*)\b", RegexOptions.IgnoreCase);

        // 3. Generation action verbs
        var hasGenVerb = Regex.IsMatch(lower, @"\b(сгенерируй\w*|сгенерить|сделай\w*|создай\w*|нарисуй\w*|нарисовать|отрисуй\w*|отрисовать|изобрази\w*|перерисуй\w*)\b", RegexOptions.IgnoreCase);

        // 4. If reference images were attached (imageCount > 0):
        if (imageCount > 0)
        {
            if (hasVisualNoun || hasGenVerb || Regex.IsMatch(lower, @"\b(стил\w*|референс\w*|похож\w*|образ\w*)\b", RegexOptions.IgnoreCase))
                return true;
        }

        // 5. Strong combinations:
        // "нарисуй ..." (e.g. "нарисуй кота", "нарисуй закат")
        if (Regex.IsMatch(lower, @"^\s*(нарисуй|отрисуй|изобрази)\b", RegexOptions.IgnoreCase))
            return true;

        // "сделай фото ...", "сгенерируй картинку ...", "создай изображение ..."
        if (hasGenVerb && hasVisualNoun)
            return true;

        // Direct triggers matching user's exact examples:
        // "сгенерируй", "сделай фото"
        if (Regex.IsMatch(lower, @"\b(сделай фото|сделай фотку|сгенерируй арт|сгенерируй фото|сгенерируй картинку|сгенерируй изображение)\b", RegexOptions.IgnoreCase))
            return true;

        return false;
    }

    private static string CleanImagePrompt(string prompt)
    {
        var clean = prompt.Trim();
        clean = Regex.Replace(clean, @"^(пожалуйста,?\s*)?(сгенерируй|создай|нарисуй|сделай|отрисуй|изобрази)(\s+(мне|нам))?(\s+(картинку|фото|фотку|фотографию|арт|изображение|рисунок|иллюстрацию))?(:|\s+чтобы|\s+где|\s+как|\s+)", "", RegexOptions.IgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(clean) ? prompt.Trim() : clean;
    }

    private async Task GenerateImageViaNanoBananaAsync(string prompt, IReadOnlyList<AssistantAttachment> attachments, string accessToken, CancellationToken token)
    {
        try
        {
            var cleanPrompt = CleanImagePrompt(prompt);
            string? imageUrl = null;
            string? errorDetail = null;

            // Step 1: Direct call using Supabase app_config table & cun.ai / NewAPI provider
            var directResult = await TryGenerateImageDirectAsync(cleanPrompt, attachments, accessToken, token);
            if (!string.IsNullOrWhiteSpace(directResult.ImageUrl))
            {
                imageUrl = directResult.ImageUrl;
            }
            else
            {
                errorDetail = directResult.ErrorMessage;

                // Step 2: Fallback to Supabase Edge Function /functions/v1/luma-image
                try
                {
                    var edgeUrl = Authentication.SupabaseOptions.ProjectUrl + "/functions/v1/luma-image";
                    using var req = new HttpRequestMessage(HttpMethod.Post, edgeUrl);
                    req.Headers.TryAddWithoutValidation("apikey", Authentication.SupabaseOptions.PublishableKey);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    var payload = new
                    {
                        prompt = cleanPrompt,
                        images = attachments.Where(a => !a.IsVideo && !string.IsNullOrWhiteSpace(a.Base64)).Take(3).Select(a => new
                        {
                            base64 = a.Base64,
                            mime = a.Mime,
                            name = a.Name
                        }).ToArray()
                    };
                    req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                    using var resp = await ImageGenHttp.SendAsync(req, token);
                    var respBody = await resp.Content.ReadAsStringAsync(token);
                    if (resp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(respBody);
                        if (doc.RootElement.TryGetProperty("imageUrl", out var urlEl) && urlEl.GetString() is { Length: > 0 } url)
                        {
                            imageUrl = url;
                        }
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                var caption = cleanPrompt.Length > 80 ? cleanPrompt[..80] + "…" : cleanPrompt;
                var markdown = $"![{caption}]({imageUrl})\n\nГотово! Изображение создано по вашему запросу.";
                PostAssistant(new { kind = "delta", text = markdown });
                _assistantHistory.Add(new AssistantMessage { Role = "assistant", Text = markdown });
                PostAssistant(new { kind = "done" });
            }
            else
            {
                if (!IsAssistantUnlimited && _state.AssistantQuota.ImageUsed > 0)
                {
                    _state.AssistantQuota.ImageUsed--;
                    _state.AssistantQuota.Used = Math.Max(0, _state.AssistantQuota.Used - 1);
                    _state.AssistantQuota.Remaining = Math.Min(_state.AssistantQuota.Limit, _state.AssistantQuota.Remaining + 1);
                    _stateStore.Save();
                    PostQuota(_state.AssistantQuota);
                }

                var errorMsg = !string.IsNullOrWhiteSpace(errorDetail)
                    ? errorDetail
                    : "⚠️ **API ключ Nano Banana не настроен.**\n\nЧтобы включить генерацию картинок:\n1. Откройте панель управления **Supabase**\n2. Перейдите в **Table Editor** → таблицу `app_config`\n3. Вставьте ваш ключ в поле `value` для строки `nanobanana_api_key`\n*(Или укажите секрет `NANOBANANA_API_KEY` в Edge Functions)*";

                PostAssistant(new { kind = "delta", text = errorMsg });
                PostAssistant(new { kind = "done" });
            }
        }
        catch (OperationCanceledException) { PostAssistant(new { kind = "done" }); }
        catch (Exception ex)
        {
            if (!IsAssistantUnlimited && _state.AssistantQuota.ImageUsed > 0)
            {
                _state.AssistantQuota.ImageUsed--;
                _state.AssistantQuota.Used = Math.Max(0, _state.AssistantQuota.Used - 1);
                _state.AssistantQuota.Remaining = Math.Min(_state.AssistantQuota.Limit, _state.AssistantQuota.Remaining + 1);
                _stateStore.Save();
                PostQuota(_state.AssistantQuota);
            }
            App.Log(ex);
            PostAssistant(new { kind = "error", text = "Ошибка генерации изображения: " + ex.Message });
        }
    }

    private async Task<(string? ImageUrl, string? ErrorMessage)> TryGenerateImageDirectAsync(
        string prompt, IReadOnlyList<AssistantAttachment> attachments, string accessToken, CancellationToken token)
    {
        try
        {
            // 1. Fetch configs from Supabase REST API
            string apiKey = "";
            string endpoint = "";
            string model = "";

            try
            {
                var configUrl = Authentication.SupabaseOptions.ProjectUrl + "/rest/v1/app_config?select=key,value";
                using var req = new HttpRequestMessage(HttpMethod.Get, configUrl);
                req.Headers.TryAddWithoutValidation("apikey", Authentication.SupabaseOptions.PublishableKey);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var resp = await ImageGenHttp.SendAsync(req, token);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(token);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var row in doc.RootElement.EnumerateArray())
                        {
                            var k = row.TryGetProperty("key", out var kVal) ? kVal.GetString() : "";
                            var v = row.TryGetProperty("value", out var vVal) ? vVal.GetString() : "";
                            if (string.IsNullOrWhiteSpace(v)) continue;
                            if (k is "nanobanana_api_key" or "nano_banana_api_key") apiKey = v.Trim();
                            else if (k is "nanobanana_endpoint" or "nano_banana_endpoint") endpoint = v.Trim();
                            else if (k is "nanobanana_model" or "nano_banana_model") model = v.Trim();
                        }
                    }
                }
            }
            catch (Exception ex) { App.Log(ex); }

            // Default fallback if not yet set in Supabase app_config
            if (string.IsNullOrWhiteSpace(apiKey)) apiKey = "sk-5OjIT9DWc4DDGCGrBvTrgjLmOSbcWLk8lNz0NBX9VifFtXzP";
            if (string.IsNullOrWhiteSpace(endpoint)) endpoint = "https://www.cun.ai";
            if (string.IsNullOrWhiteSpace(model)) model = "gemini-3.1-flash-image-preview";

            // Auto-detect NewAPI / cun.ai key (sk-...)
            if (apiKey.StartsWith("sk-") && endpoint.Contains("kie.ai"))
            {
                endpoint = "https://www.cun.ai";
                if (model is "nano-banana-2" or "nano-banana-pro" or "nano-banana-2-lite" or "" or "gemini-3-pro-image-preview")
                    model = "gemini-3.1-flash-image-preview";
            }

            var refImages = attachments.Where(a => !a.IsVideo && !string.IsNullOrWhiteSpace(a.Base64)).Take(3).ToList();

            // ==========================================
            // Provider Branch 1: NewAPI / OpenAI / cun.ai
            // ==========================================
            bool isOpenAiOrNewApi = endpoint.Contains("cun.ai") ||
                                    endpoint.Contains("/chat/completions") ||
                                    endpoint.Contains("/images/generations") ||
                                    (!endpoint.Contains("kie.ai") && !endpoint.Contains("createTask")) ||
                                    apiKey.StartsWith("sk-");

            if (isOpenAiOrNewApi)
            {
                var baseApi = endpoint.TrimEnd('/');
                string chatUrl;
                if (baseApi.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                    chatUrl = baseApi;
                else if (baseApi.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                    chatUrl = baseApi + "/chat/completions";
                else
                    chatUrl = baseApi + "/v1/chat/completions";

                // Prepare multimodal messages with optional references
                object contentObj;
                if (refImages.Count > 0)
                {
                    var contentParts = new List<object>
                    {
                        new { type = "text", text = $"Generate an image: {prompt}" }
                    };
                    foreach (var img in refImages)
                    {
                        var mime = img.Mime.Contains("png") ? "image/png" : img.Mime.Contains("webp") ? "image/webp" : "image/jpeg";
                        contentParts.Add(new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:{mime};base64,{img.Base64}" }
                        });
                    }
                    contentObj = contentParts.ToArray();
                }
                else
                {
                    contentObj = $"Generate an image: {prompt}";
                }

                var payload = new
                {
                    model = model,
                    messages = new object[]
                    {
                        new { role = "user", content = contentObj }
                    }
                };

                using var chatReq = new HttpRequestMessage(HttpMethod.Post, chatUrl);
                chatReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                chatReq.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var chatResp = await ImageGenHttp.SendAsync(chatReq, token);
                var chatBody = await chatResp.Content.ReadAsStringAsync(token);

                if (chatResp.IsSuccessStatusCode)
                {
                    using var chatDoc = JsonDocument.Parse(chatBody);
                    var choices = chatDoc.RootElement.TryGetProperty("choices", out var ch) ? ch : default;
                    if (choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];
                        if (firstChoice.TryGetProperty("message", out var msgEl) && msgEl.TryGetProperty("content", out var cntEl))
                        {
                            var textContent = cntEl.GetString() ?? "";
                            // Match markdown image ![...](...)
                            var mdMatch = Regex.Match(textContent, @"!\[([^\]]*)\]\(([^)]+)\)");
                            if (mdMatch.Success)
                            {
                                return (mdMatch.Groups[2].Value.Trim(), null);
                            }
                            // Match direct URL or data URI
                            var directUrlMatch = Regex.Match(textContent, @"(https?://\S+\.(?:png|jpe?g|webp|gif\S*)|data:image/[^)\s]+)", RegexOptions.IgnoreCase);
                            if (directUrlMatch.Success)
                            {
                                return (directUrlMatch.Groups[1].Value.Trim(), null);
                            }
                            if (textContent.StartsWith("http") || textContent.StartsWith("data:image/"))
                            {
                                return (textContent.Trim(), null);
                            }
                            return (null, "Ответ API: " + (textContent.Length > 200 ? textContent[..200] + "…" : textContent));
                        }
                    }
                }
                else
                {
                    // Fallback to /images/generations endpoint if chat completions not supported
                    try
                    {
                        string imgGenUrl = baseApi.Replace("/chat/completions", "");
                        if (!imgGenUrl.EndsWith("/images/generations", StringComparison.OrdinalIgnoreCase))
                        {
                            if (imgGenUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) imgGenUrl += "/images/generations";
                            else imgGenUrl += "/v1/images/generations";
                        }

                        var imgPayload = new
                        {
                            prompt = prompt,
                            model = model,
                            n = 1,
                            size = "1024x1024"
                        };
                        using var imgReq = new HttpRequestMessage(HttpMethod.Post, imgGenUrl);
                        imgReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                        imgReq.Content = new StringContent(JsonSerializer.Serialize(imgPayload), Encoding.UTF8, "application/json");
                        using var imgResp = await ImageGenHttp.SendAsync(imgReq, token);
                        var imgBody = await imgResp.Content.ReadAsStringAsync(token);
                        if (imgResp.IsSuccessStatusCode)
                        {
                            using var imgDoc = JsonDocument.Parse(imgBody);
                            if (imgDoc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array && dataArr.GetArrayLength() > 0)
                            {
                                var item = dataArr[0];
                                if (item.TryGetProperty("url", out var uEl)) return (uEl.GetString(), null);
                                if (item.TryGetProperty("b64_json", out var bEl)) return ($"data:image/png;base64,{bEl.GetString()}", null);
                            }
                        }
                    }
                    catch { }

                    return (null, $"Ошибка API генерации ({chatResp.StatusCode}): {chatBody}");
                }
            }

            // ==========================================
            // Provider Branch 2: Kie.ai createTask API
            // ==========================================
            var referenceUrls = new List<string>();
            for (var i = 0; i < refImages.Count; i++)
            {
                var att = refImages[i];
                var fileName = $"ref_{Guid.NewGuid():N}_{i}.jpg";
                try
                {
                    var uploadUrl = $"{Authentication.SupabaseOptions.ProjectUrl}/storage/v1/object/assistant-images/{fileName}";
                    using var upReq = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
                    upReq.Headers.TryAddWithoutValidation("apikey", Authentication.SupabaseOptions.PublishableKey);
                    upReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    var bytes = Convert.FromBase64String(att.Base64);
                    upReq.Content = new ByteArrayContent(bytes);
                    upReq.Content.Headers.ContentType = new MediaTypeHeaderValue(att.Mime.Contains("png") ? "image/png" : "image/jpeg");
                    using var upResp = await ImageGenHttp.SendAsync(upReq, token);
                    if (upResp.IsSuccessStatusCode)
                    {
                        var pubUrl = $"{Authentication.SupabaseOptions.ProjectUrl}/storage/v1/object/public/assistant-images/{fileName}";
                        referenceUrls.Add(pubUrl);
                    }
                    else
                    {
                        referenceUrls.Add($"data:{att.Mime};base64,{att.Base64}");
                    }
                }
                catch
                {
                    referenceUrls.Add($"data:{att.Mime};base64,{att.Base64}");
                }
            }

            using var taskReq = new HttpRequestMessage(HttpMethod.Post, endpoint);
            taskReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            var payloadObj = new
            {
                model = model,
                input = new
                {
                    prompt = prompt,
                    image_input = referenceUrls,
                    aspect_ratio = "1:1"
                }
            };
            taskReq.Content = new StringContent(JsonSerializer.Serialize(payloadObj), Encoding.UTF8, "application/json");
            using var taskResp = await ImageGenHttp.SendAsync(taskReq, token);
            var taskBody = await taskResp.Content.ReadAsStringAsync(token);
            if (!taskResp.IsSuccessStatusCode)
            {
                return (null, $"Ошибка вызова API Nano Banana ({taskResp.StatusCode}): {taskBody}");
            }

            using var taskDoc = JsonDocument.Parse(taskBody);
            var rootEl = taskDoc.RootElement;
            string? taskId = null;
            if (rootEl.TryGetProperty("data", out var dataEl) && dataEl.TryGetProperty("taskId", out var tEl))
                taskId = tEl.GetString();
            else if (rootEl.TryGetProperty("taskId", out var tEl2))
                taskId = tEl2.GetString();

            if (string.IsNullOrWhiteSpace(taskId))
            {
                if (rootEl.TryGetProperty("data", out var dEl) && dEl.TryGetProperty("imageUrl", out var imgEl))
                    return (imgEl.GetString(), null);
                if (rootEl.TryGetProperty("imageUrl", out var imgEl2))
                    return (imgEl2.GetString(), null);
                return (null, "API Nano Banana не вернуло идентификатор задачи: " + taskBody);
            }

            var recordUrl = endpoint.Replace("/createTask", "/recordInfo") + $"?taskId={taskId}";
            for (var attempt = 0; attempt < 35; attempt++)
            {
                await Task.Delay(1400, token);
                using var pollReq = new HttpRequestMessage(HttpMethod.Get, recordUrl);
                pollReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using var pollResp = await ImageGenHttp.SendAsync(pollReq, token);
                if (!pollResp.IsSuccessStatusCode) continue;

                var pollBody = await pollResp.Content.ReadAsStringAsync(token);
                using var pollDoc = JsonDocument.Parse(pollBody);
                var pRoot = pollDoc.RootElement;
                var pData = pRoot.TryGetProperty("data", out var pd) ? pd : pRoot;
                var state = pData.TryGetProperty("state", out var sEl) ? sEl.GetString() : "";

                if (state == "success")
                {
                    string? finalUrl = null;
                    if (pData.TryGetProperty("resultUrl", out var rEl)) finalUrl = rEl.GetString();
                    else if (pData.TryGetProperty("result", out var resObj) && resObj.TryGetProperty("image_url", out var rImg)) finalUrl = rImg.GetString();
                    else if (pData.TryGetProperty("images", out var arrEl) && arrEl.ValueKind == JsonValueKind.Array && arrEl.GetArrayLength() > 0) finalUrl = arrEl[0].GetString();

                    if (!string.IsNullOrWhiteSpace(finalUrl))
                        return (finalUrl, null);
                }
                else if (state is "failed" or "error")
                {
                    var err = pData.TryGetProperty("error", out var eEl) ? eEl.GetString() : "Генерация завершилась с ошибкой со стороны Nano Banana.";
                    return (null, err);
                }
            }

            return (null, "Время ожидания генерации Nano Banana истекло.");
        }
        catch (Exception ex)
        {
            return (null, "Ошибка прямого подключения: " + ex.Message);
        }
    }

    private Task AskAssistantAsync(string question, string? attachmentBase64, string? attachmentMime, string? attachmentName, bool isAgentExplicit = false)
    {
        var list = new List<AssistantAttachment>();
        if (!string.IsNullOrWhiteSpace(attachmentBase64))
            list.Add(new AssistantAttachment { Base64 = attachmentBase64, Mime = attachmentMime ?? "image/jpeg", Name = attachmentName ?? "image" });
        return AskAssistantAsync(question, list, isAgentExplicit);
    }

    private async Task AskAssistantAsync(string question, IReadOnlyList<AssistantAttachment>? attachments = null, bool isAgentExplicit = false)
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

        var attList = attachments ?? [];

        // Image generation intent trigger
        if (!isAgentExplicit && IsImageGenerationRequest(question, attList.Count))
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (_state.AssistantQuota.ImageQuotaDate != today)
            {
                _state.AssistantQuota.ImageQuotaDate = today;
                _state.AssistantQuota.ImageUsed = 0;
                _stateStore.Save();
            }

            var isUnlimited = IsAssistantUnlimited;

            // 1. Check general daily request quota (if not unlimited)
            if (!isUnlimited && _state.AssistantQuota.Remaining <= 0)
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

            // 2. Check 2 photos per day limit for non-unlimited accounts
            if (!isUnlimited && _state.AssistantQuota.ImageUsed >= _state.AssistantQuota.ImageLimit)
            {
                var imageLimitMsg = _state.Language switch
                {
                    "uk" => "Денний ліміт генерації зображень вичерпано (2 з 2 на день). Оновлення щодня. Звичайний текстовий асистент залишається доступним.",
                    "en" => "Daily image generation limit reached (2 of 2 today). Resets daily. Regular text assistant remains available.",
                    _ => "Дневной лимит генерации изображений исчерпан (2 из 2 в день). Обычный текстовый ассистент LumaAI остаётся доступен."
                };
                PostAssistant(new { kind = "error", text = imageLimitMsg });
                _assistantRun = null;
                return;
            }

            // Deduct 1 from daily image quota and 1 from overall request quota
            if (!isUnlimited)
            {
                _state.AssistantQuota.ImageUsed++;
                _state.AssistantQuota.Used++;
                _state.AssistantQuota.Remaining = Math.Max(0, _state.AssistantQuota.Remaining - 1);
                _stateStore.Save();
                PostQuota(_state.AssistantQuota);
            }

            PostAssistant(new { kind = "delta", text = "🎨 *Генерирую изображение по вашему запросу...*\n\n" });
            _assistantHistory.Add(new AssistantMessage { Role = "user", Text = question });

            try
            {
                await GenerateImageViaNanoBananaAsync(question, attList, accessToken, run.Token);
            }
            finally
            {
                if (ReferenceEquals(_assistantRun, run)) _assistantRun = null;
            }
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

            var isUnlimited = IsAssistantUnlimited;

            // Check overall daily 15-request quota
            if (!isUnlimited && _state.AssistantQuota.Remaining <= 0)
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
            if (!isUnlimited && _state.AssistantQuota.AgentUsed >= _state.AssistantQuota.AgentLimit)
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
            if (!isUnlimited)
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

                string? effectiveImage = attList.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Base64))?.Base64;
                string? effectiveMime = attList.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Base64))?.Mime;
                string? effectiveName = attList.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Base64))?.Name;

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
                        var isOwner = IsOwnerAccount;
                        _state.AssistantQuota.Limit = quota.Limit;
                        _state.AssistantQuota.Used = quota.Used;
                        _state.AssistantQuota.Remaining = isOwner || quota.Unlimited ? 999 : quota.Remaining;
                        _state.AssistantQuota.Unlimited = quota.Unlimited || isOwner;
                        _stateStore.Save();
                        PostQuota(_state.AssistantQuota);
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
        foreach (var att in attList.Where(a => !a.IsVideo && !string.IsNullOrWhiteSpace(a.Base64) && a.Base64.Length <= 8_000_000).Take(3))
        {
            var mime = att.Mime is "image/png" or "image/webp" or "image/gif" ? att.Mime : "image/jpeg";
            images.Add(new AssistantImage
            {
                Base64 = att.Base64,
                MimeType = mime,
                Name = string.IsNullOrWhiteSpace(att.Name) ? "attachment" : att.Name[..Math.Min(att.Name.Length, 120)]
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
                var isOwner = IsOwnerAccount;
                _state.AssistantQuota.Limit = quota.Limit;
                _state.AssistantQuota.Used = quota.Used;
                _state.AssistantQuota.Remaining = isOwner || quota.Unlimited ? 999 : quota.Remaining;
                _state.AssistantQuota.Unlimited = quota.Unlimited || isOwner;
                _state.AssistantQuota.UpdatedAt = _clock.UtcNow;
                _stateStore.Save();
                PostQuota(_state.AssistantQuota);
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

        // Autonomous Browser Agent: if the user commands an action inside the browser (solve quiz/test, fill form, etc.)
        if (BrowserAgent.BrowserAgentRunner.IsAgentGoal(question))
        {
            BringToFrontAndActivate();
            try
            {
                var conversation = string.Join("\n", _voiceAssistantHistory.TakeLast(6)
                    .Select(m => (m.Role == "user" ? "Пользователь: " : "Luma: ") + (m.Text.Length > 400 ? m.Text[..400] + "…" : m.Text)));

                var agentResult = await BrowserAgent.BrowserAgentRunner.ExecuteAgentTaskAsync(
                    question,
                    this,
                    tokenAuth,
                    _state.Language,
                    async delta =>
                    {
                        onDelta(delta);
                        await Task.CompletedTask;
                    },
                    token,
                    conversation,
                    quota => Dispatcher.Invoke(() =>
                    {
                        var isOwner = IsOwnerAccount;
                        _state.AssistantQuota.Limit = quota.Limit;
                        _state.AssistantQuota.Used = quota.Used;
                        _state.AssistantQuota.Remaining = isOwner || quota.Unlimited ? 999 : quota.Remaining;
                        _state.AssistantQuota.Unlimited = quota.Unlimited || isOwner;
                        _stateStore.Save();
                        PostQuota(_state.AssistantQuota);
                    }),
                    preferredTier: "fast");

                if (!string.IsNullOrWhiteSpace(agentResult))
                {
                    _voiceAssistantHistory.Add(new AssistantMessage { Role = "user", Text = question });
                    _voiceAssistantHistory.Add(new AssistantMessage { Role = "assistant", Text = agentResult });
                    while (_voiceAssistantHistory.Count > 20) _voiceAssistantHistory.RemoveAt(0);
                }
                BringToFrontAndActivate();
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                App.Log(ex);
            }
        }

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
        context.AppendLine("Ты — LumaAI, высокоинтеллектуальный голосовой ассистент браузера Luma с компьютерным зрением уровня Gemini Live и Project Astra.");
        context.AppendLine("ТЕБЕ ПЕРЕДАН АКТУАЛЬНЫЙ СНИМОК ЭКРАНА ПОЛЬЗОВАТЕЛЯ в момент вопроса.");
        context.AppendLine("МЕСТОПОЛОЖЕНИЕ КУРСОРА МЫШИ ПОЛЬЗОВАТЕЛЯ ОТМЕЧЕНО КРАСНЫМ КРУЖКОМ-ПРИЦЕЛОМ НА СНИМКЕ ЭКРАНА.");
        context.AppendLine("Ты обладаешь глубоким аналитическим мышлением, широким кругозором и безупречным распознаванием деталей экрана:");
        context.AppendLine("1. ЕСЛИ ВОПРОС «ЧТО ЭТО?», «ЧТО ТУТ НАПИСАНО?», «ПОМОГИ», «ПЕРЕВЕДИ»: в первую очередь внимательно изучи объект прямо под курсором мыши (красным кружком) или рядом с ним. Прочитай весь видимый текст, цифры, названия предметов, баффы/дебаффы, ошибки или кнопки.");
        context.AppendLine("2. В ИГРАХ: точно распознавай конкретную игру (Dota 2, CS2, Minecraft, Genshin Impact, Cyberpunk, Witcher и т.д.), персонажа, здоровье/ману, инвентарь, карту, квесты и давай конкретные, тактически верные и экспертные советы по прохождению, сборкам и крафту.");
        context.AppendLine("3. В КОДЕ И ПРОГРАММАХ: читай текст ошибки, стектрейс или строчку кода и сразу давай готовое решение без лишних рассуждений.");
        context.AppendLine("4. В ИНТЕРФЕЙСАХ И ДОКУМЕНТАХ: переводи иностранный текст, объясняй пункты меню, помогай с заполнением форм.");
        context.AppendLine("5. СТИЛЬ ОТВЕТА: Умный, экспертный, лаконичный и доброжелательный разговорный язык. Отвечай прямо на вопрос, сразу давая решение без пустых предисловий («Я вижу на снимке...», «Судя по всему...»).");
        context.AppendLine("6. ПРАВИЛО ИНТЕРНЕТА: Ты сам находишь актуальную информацию в интернете. НИКОГДА не открывай поисковики во вкладках браузера. Отвечай на вопросы пользователя текстом прямо в виджете.");
        context.AppendLine("7. ЕСЛИ ПОЛЬЗОВАТЕЛЬ ПРОСИТ ОТКРЫТЬ САЙТ, ВКЛЮЧИТЬ ВИДЕО, ПЕСНЮ, ФИЛЬМ ИЛИ АНИМЕ («открой видео X», «включи X», «открой ютуб», «найди на кинопоиске»):");
        context.AppendLine("   - КАТЕГОРИЧЕСКИ ЗАПРЕЩЕНО задавать уточняющие или встречные вопросы (НЕ спрашивай «какое видео?», «какой канал?», «какой трек?»).");
        context.AppendLine("   - СРАЗУ открывай без лишних вопросов! Назови действие в 1 коротком предложении и мгновенно выдай блок %%LUMA%%.");
        context.AppendLine("   - Для видео: find video | <имя автора или название ролика>");
        context.AppendLine("   - Для музыки: find music | <исполнитель или трек>");
        context.AppendLine("   - Для аниме: find anime | <название>");
        context.AppendLine("   - Для фильмов: find movie | <название>");
        context.AppendLine("   - Для сайтов: open <url>");
        context.AppendLine(AssistantActions.Protocol);

        if (!string.IsNullOrWhiteSpace(webSnippets))
        {
            context.AppendLine("\n[Свежие актуальные факты из фонового поиска интернета]:");
            context.AppendLine(webSnippets);
            context.AppendLine("Используй эти факты как свои собственные уверенные знания.");
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
