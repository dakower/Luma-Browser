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
    private void ReceiveWebMessage(BrowserTab tab, WebView2 view, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                using var innerDoc = JsonDocument.Parse(root.GetString() ?? "{}");
                root = innerDoc.RootElement.Clone();
            }
            var kind = root.TryGetProperty("kind", out var value) ? value.GetString() : "";
            var settingsPage = tab.IsInternal && tab.InternalPageKind == "luma://settings";
            var importPage = tab.IsInternal && tab.InternalPageKind == "luma://import";
            var searchPage = tab.IsInternal && tab.InternalPageKind.StartsWith("luma://search", StringComparison.Ordinal);
            if (kind == "luma-network-retry")
            {
                Dispatcher.Invoke(() => RetryNetworkPage(tab, view));
                return;
            }
            if (kind == "luma-search-request")
            {
                if (!searchPage) { App.Warn("Blocked search message from web content"); return; }
                var query = root.TryGetProperty("query", out value) ? value.GetString() ?? "" : "";
                var searchMode = root.TryGetProperty("mode", out value) ? value.GetString() ?? "all" : "all";
                var requestId = root.TryGetProperty("requestId", out value) && value.TryGetInt32(out var id) ? id : 0;
                var searchHistory = root.TryGetProperty("history", out value) && value.ValueKind == JsonValueKind.Array ? value.GetRawText() : "[]";
                Dispatcher.BeginInvoke(async () => await RunLumaSearchAsync(view, query, searchMode, requestId, searchHistory)); return;
            }
            if (kind == "luma-search-open")
            {
                if (!searchPage) return;
                var target = root.TryGetProperty("url", out value) ? value.GetString() ?? "" : "";
                if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                    Dispatcher.BeginInvoke(async () => await NavigateFromLumaSearchAsync(tab, target));
                return;
            }
            if (kind == "luma-search-open-new")
            {
                if (!searchPage) return;
                var target = root.TryGetProperty("url", out value) ? value.GetString() ?? "" : "";
                if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                    Dispatcher.BeginInvoke(async () => await AddTabAsync(target));
                return;
            }
            if (kind == "luma-search-bad")
            {
                if (!searchPage) { App.Warn("Blocked search feedback from web content"); return; }
                var feedback = root.Clone();
                Dispatcher.BeginInvoke(async () => await SendBadSearchFeedbackAsync(view, feedback)); return;
            }
            if (kind == "luma-search-state")
            {
                if (!searchPage) return;
                var query = root.TryGetProperty("query", out value) ? value.GetString() ?? "" : "";
                var searchMode = root.TryGetProperty("mode", out value) ? value.GetString() ?? "all" : "all";
                Dispatcher.Invoke(() => { RecordTesterAction($"Выбрана категория поиска: {searchMode}"); var address = "luma://search?q=" + Uri.EscapeDataString(query) + "&mode=" + Uri.EscapeDataString(searchMode); tab.FullUrl = address; tab.InternalPageKind = address; tab.Title = query + " — Luma Search"; UpdateChrome(); });
                return;
            }
            if (kind == "luma-settings") { if (!settingsPage) { App.Warn("Blocked settings message from web content"); return; } Dispatcher.Invoke(() => ApplySettings(root)); return; }
            if (kind == "luma-theme-preview") { if (!settingsPage) return; var theme = root.TryGetProperty("theme", out value) ? value.GetString() ?? "purple" : "purple"; Dispatcher.Invoke(() => PreviewTheme(tab, theme)); return; }
            if (kind == "luma-theme-preview-cancel") { if (settingsPage) Dispatcher.Invoke(CancelThemePreview); return; }
            if (kind == "luma-check-updates") { if (!settingsPage) return; Dispatcher.BeginInvoke(async () => await CheckForUpdatesManuallyAsync()); return; }
            if (kind == "luma-open-data" && settingsPage) { Dispatcher.Invoke(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LumaState.DirectoryPath) { UseShellExecute = true })); return; }
            if (kind == "luma-download-folder") { if (!settingsPage) return; Dispatcher.BeginInvoke(async () => await ChooseDownloadFolderAsync(view)); return; }
            if (kind == "luma-download-open" && settingsPage) { var id = root.TryGetProperty("id", out value) ? value.GetString() ?? "" : ""; Dispatcher.Invoke(() => OpenDownloadById(id)); return; }
            if (kind == "luma-download-reveal" && settingsPage) { var id = root.TryGetProperty("id", out value) ? value.GetString() ?? "" : ""; Dispatcher.Invoke(() => RevealDownloadById(id)); return; }
            if (kind == "luma-download-remove" && settingsPage) { var id = root.TryGetProperty("id", out value) ? value.GetString() ?? "" : ""; Dispatcher.Invoke(() => RemoveDownloadById(id)); return; }
            if (kind == "luma-download-clear" && settingsPage) { Dispatcher.Invoke(ClearDownloadHistory); return; }
            if (kind == "luma-import")
            {
                if (!importPage || ((App)Application.Current).IsPrivateSession) { App.Warn("Blocked import message"); return; }
                bool B(string name) => root.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.True;
                var options = new ChromeImportOptions(B("history"), B("bookmarks"), B("passwords"), B("autofill"), B("openTabs"), B("searchEngine"), B("cookies"));
                Dispatcher.BeginInvoke(async () => await RunChromeImportAsync(view, options)); return;
            }
            if (kind == "luma-pane-focus") { Dispatcher.Invoke(() => ActivatePane(tab, view)); return; }
            if (kind == "luma-translate-more")
            {
                var domain = TranslateDomain(view.Source?.ToString() ?? "");
                if (domain is not null && (_state.AlwaysTranslateDomains.Contains(domain) || IsPageTranslationActive(view)))
                    Dispatcher.BeginInvoke(async () => await TranslateRoundAsync(view));
                return;
            }
            if (kind == "luma-history-open" && settingsPage)
            {
                var openUrl = root.TryGetProperty("url", out value) ? value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(openUrl)) Dispatcher.BeginInvoke(async () => await AddTabAsync(openUrl));
                return;
            }
            if (kind == "luma-history-remove" && settingsPage)
            {
                var id = root.TryGetProperty("id", out value) ? value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id)) Dispatcher.Invoke(() => { _state.History.RemoveAll(h => h.Id == id); _stateStore.Save(); RefreshHistoryView(); });
                return;
            }
            if (kind == "luma-history-clear" && settingsPage) { Dispatcher.Invoke(() => { _state.History.Clear(); _stateStore.Save(); RefreshHistoryView(); }); return; }
            if (kind == "luma-import-csv")
            {
                if (!importPage || ((App)Application.Current).IsPrivateSession) { App.Warn("Blocked CSV import message"); return; }
                var csv = root.TryGetProperty("csv", out value) ? value.GetString() ?? "" : "";
                _ = Dispatcher.BeginInvoke(() =>
                {
                    var count = CredentialVault.ImportCredentialsFromCsv(csv, out var warning);
                    var payload = JsonSerializer.Serialize(new { count, warning });
                    _ = view.CoreWebView2?.ExecuteScriptAsync($"window.lumaImportCsvResult && window.lumaImportCsvResult({payload})");
                });
                return;
            }
            if (kind == "luma-media-state")
            {
                var playing = root.TryGetProperty("playing", out value) && value.ValueKind == JsonValueKind.True;
                string S(string name) => root.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
                double D(string name) => root.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? d : 0;
                var title = S("title"); var artist = S("artist"); var artwork = S("artwork"); var mediaType = S("mediaType");
                var position = D("position"); var duration = D("duration"); var volume = D("volume");
                Dispatcher.Invoke(() => UpdateMediaState(tab, playing, title, artist, artwork, position, duration, volume, mediaType == "video"));
                return;
            }
            if (kind == "luma-prompt-install-extension")
            {
                var extId = root.TryGetProperty("id", out value) ? value.GetString() : null;
                var title = root.TryGetProperty("title", out value) ? value.GetString() : null;
                var icon = root.TryGetProperty("icon", out value) ? value.GetString() : null;
                var source = (root.TryGetProperty("source", out value) ? value.GetString() : null) ?? "chrome";
                var manifestJson = root.TryGetProperty("manifest", out value) ? value.GetString() : null;

                if (!string.IsNullOrWhiteSpace(extId))
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        var profile = tab.View.CoreWebView2?.Profile ?? ExtensionManager.Profile;
                        List<string>? perms = null;
                        if (!string.IsNullOrWhiteSpace(manifestJson))
                        {
                            try { perms = ExtensionManager.ParsePermissions(manifestJson); } catch { }
                        }
                        PromptInstallFromStore(extId, title, icon, source, profile, perms);
                    });
                }
                return;
            }
            if (kind == "luma-install-extension")
            {
                var extId = root.TryGetProperty("id", out value) ? value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(extId))
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        var profile = tab.View.CoreWebView2?.Profile ?? ExtensionManager.Profile;
                        PromptInstallFromStore(extId, null, null, "chrome", profile);
                    });
                }
                return;
            }
            if (kind == "luma-toggle-extension" && settingsPage)
            {
                var extId = root.TryGetProperty("id", out value) ? value.GetString() : null;
                var enable = root.TryGetProperty("enable", out value) && value.GetBoolean();
                if (!string.IsNullOrWhiteSpace(extId))
                {
                    Dispatcher.BeginInvoke(async () =>
                    {
                        var profile = tab.View.CoreWebView2?.Profile ?? ExtensionManager.Profile;
                        await ExtensionManager.ToggleExtensionAsync(profile, extId, enable);
                    });
                }
                return;
            }
            if (kind == "luma-remove-extension" && settingsPage)
            {
                var extId = root.TryGetProperty("id", out value) ? value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(extId))
                {
                    Dispatcher.BeginInvoke(async () =>
                    {
                        var profile = tab.View.CoreWebView2?.Profile ?? ExtensionManager.Profile;
                        await ExtensionManager.RemoveExtensionAsync(profile, extId);
                        ShowToast("Расширение удалено", "Расширение успешно удалено");
                        await view.CoreWebView2.ExecuteScriptAsync($"window.location.reload()");
                    });
                }
                return;
            }
            if (kind == "luma-import-extension" && settingsPage)
            {
                Dispatcher.BeginInvoke(async () =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Filter = "Расширения Chromium (*.crx;*.zip)|*.crx;*.zip|Все файлы (*.*)|*.*",
                        Title = "Выберите файл расширения"
                    };
                    if (dlg.ShowDialog() == true)
                    {
                        var profile = tab.View.CoreWebView2?.Profile;
                        if (profile != null)
                        {
                            var meta = await ExtensionManager.InstallFromLocalFileAsync(profile, dlg.FileName);
                            if (meta != null)
                            {
                                ShowToast("Расширение импортировано", meta.Name);
                                await view.CoreWebView2.ExecuteScriptAsync($"window.location.reload()");
                            }
                            else
                            {
                                ShowToast("Ошибка импорта", "Не удалось прочитать файл расширения", true);
                            }
                        }
                    }
                });
                return;
            }
            if (kind == "luma-autourok-ocr-schedule")
            {
                var imageB64 = root.TryGetProperty("imageBase64", out value) ? value.GetString() : null;
                var mime = (root.TryGetProperty("mimeType", out value) ? value.GetString() : null) ?? "image/png";
                if (!string.IsNullOrWhiteSpace(imageB64))
                {
                    Dispatcher.BeginInvoke(async () => await ParseScheduleImageAsync(view, imageB64, mime));
                }
                return;
            }
            if (kind == "luma-autourok-pick-image")
            {
                Dispatcher.BeginInvoke(async () =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Filter = "Все файлы (*.*)|*.*|Изображения (*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.jfif)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.jfif;*.PNG;*.JPG;*.JPEG;*.WEBP;*.BMP;*.JFIF",
                        FilterIndex = 1,
                        Title = "Выберите фото расписания уроков и звонков"
                    };
                    if (dlg.ShowDialog(this) == true)
                    {
                        try
                        {
                            var bytes = await File.ReadAllBytesAsync(dlg.FileName);
                            var b64 = Convert.ToBase64String(bytes);
                            var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                            var mime = ext switch
                            {
                                ".png" => "image/png",
                                ".webp" => "image/webp",
                                ".bmp" => "image/bmp",
                                _ => "image/jpeg"
                            };
                            var previewPayload = JsonSerializer.Serialize(new
                            {
                                kind = "luma-autourok-preview-image",
                                fileName = Path.GetFileName(dlg.FileName),
                                fileSize = bytes.Length,
                                dataUrl = $"data:{mime};base64,{b64}",
                                imageBase64 = b64,
                                mimeType = mime
                            });
                            if (view?.CoreWebView2 is not null)
                            {
                                view.CoreWebView2.PostWebMessageAsJson(previewPayload);
                                await view.CoreWebView2.ExecuteScriptAsync($"window.__lumaShowPreview && window.__lumaShowPreview({previewPayload})");
                            }
                            await ParseScheduleImageAsync(view, b64, mime);
                        }
                        catch (Exception ex)
                        {
                            ShowToast("Ошибка чтения файла", ex.Message, true);
                        }
                    }
                    else
                    {
                        var cancelPayload = JsonSerializer.Serialize(new
                        {
                            kind = "luma-autourok-picker-cancelled"
                        });
                        if (view?.CoreWebView2 is not null)
                        {
                            view.CoreWebView2.PostWebMessageAsJson(cancelPayload);
                            await view.CoreWebView2.ExecuteScriptAsync($"window.__lumaShowPreview && window.__lumaShowPreview({cancelPayload})");
                        }
                    }
                });
                return;
            }
            if (kind != "luma-context") return;
            var mode = root.TryGetProperty("mode", out value) ? value.GetString() ?? "page" : "page";
            var link = root.TryGetProperty("link", out value) ? value.GetString() ?? "" : "";
            var image = root.TryGetProperty("image", out value) ? value.GetString() ?? "" : "";
            Dispatcher.Invoke(() => { ActivatePane(tab, view); ShowPageMenu(tab, view, mode, link, image); });
        }
        catch { }
    }

    private async Task ParseScheduleImageAsync(WebView2 view, string imageB64, string mime)
    {
        try
        {
            var accessToken = await _auth.GetAccessTokenAsync(CancellationToken.None);
            var token = !string.IsNullOrWhiteSpace(accessToken) ? accessToken : Authentication.SupabaseOptions.PublishableKey;

            var prompt = """
Ты — интеллектуальный ассистент браузера Luma. Твоя задача — извлечь школьное расписание уроков и звонков из фото или скриншота.
Внимательно прочитай дни недели (Пн, Вт, Ср, Чт, Пт, Сб), список уроков и время каждого урока (звонки).
Если на фото есть отдельное расписание звонков (например: 1 урок: 08:30-09:15, 2 урок: 09:25-10:10...), сопоставь каждый урок с соответствующим временем звонка.
Если указан только один день или дни не подписаны, заполни расписание для текущего или указанного дня.

Верни ИСКЛЮЧИТЕЛЬНО валидный JSON объект следующей структуры без каких-либо пояснений, без markdown разметки:
{
  "1": [
    {"subject": "Алгебра", "start": "08:30", "end": "09:15"},
    {"subject": "Геометрия", "start": "09:25", "end": "10:10"}
  ],
  "2": [
    {"subject": "Физика", "start": "08:30", "end": "09:15"}
  ],
  "3": [],
  "4": [],
  "5": [],
  "6": []
}
Ключи дней недели: "1" - Понедельник, "2" - Вторник, "3" - Среда, "4" - Четверг, "5" - Пятница, "6" - Суббота.
Названия предметов приводи к стандартному понятному виду (например: "Алгебра", "Геометрия", "Зарубежная литература", "Английский", "Химия", "Биология", "Физика", "Украинский язык", "Украинская литература", "Физкультура", "История Украины", "Всемирная история", "География", "Технологии", "Искусство").
""";

            var request = new List<AssistantMessage>
            {
                new() { Role = "system", Text = prompt },
                new()
                {
                    Role = "user",
                    Text = "Распознай расписание уроков и звонков на этом фото и верни строго JSON.",
                    Images = [new AssistantImage { Base64 = imageB64, MimeType = mime, Name = "schedule.png" }]
                }
            };

            var sb = new StringBuilder();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(50));
            await AssistantClient.StreamAsync(token, "fast", request, delta =>
            {
                sb.Append(delta);
                return Task.CompletedTask;
            }, cts.Token);

            var raw = sb.ToString().Trim();
            if (raw.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) raw = raw[7..];
            if (raw.StartsWith("```")) raw = raw[3..];
            if (raw.EndsWith("```")) raw = raw[..^3];
            raw = raw.Trim();

            var startIdx = raw.IndexOf('{');
            var endIdx = raw.LastIndexOf('}');
            if (startIdx >= 0 && endIdx > startIdx)
            {
                raw = raw.Substring(startIdx, endIdx - startIdx + 1);
            }

            using var doc = JsonDocument.Parse(raw);

            var resultPayload = JsonSerializer.Serialize(new
            {
                kind = "luma-autourok-ocr-result",
                success = true,
                schedule = doc.RootElement
            });

            if (view?.CoreWebView2 is not null)
            {
                view.CoreWebView2.PostWebMessageAsJson(resultPayload);
                await view.CoreWebView2.ExecuteScriptAsync($"window.__lumaApplyOcr && window.__lumaApplyOcr({resultPayload})");
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
            var errPayload = JsonSerializer.Serialize(new
            {
                kind = "luma-autourok-ocr-result",
                success = false,
                error = "Не удалось распознать расписание: " + ex.Message
            });
            try
            {
                if (view?.CoreWebView2 is not null)
                {
                    view.CoreWebView2.PostWebMessageAsJson(errPayload);
                    await view.CoreWebView2.ExecuteScriptAsync($"window.__lumaApplyOcr && window.__lumaApplyOcr({errPayload})");
                }
            }
            catch { }
        }
    }
}
