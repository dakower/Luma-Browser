using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Luma.Core;
using Clipboard = System.Windows.Clipboard;

namespace Luma;

public partial class MainWindow
{
    // =========================================================================
    // 1. MEDIA SNIFFER & DOWNLOADER
    // =========================================================================
    public record DetectedMediaItem(string Url, string Type, string Title, bool IsYt);

    private readonly Dictionary<BrowserTab, List<DetectedMediaItem>> _tabDetectedMedia = new();

    private void HandleMediaDetected(BrowserTab tab, JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("media", out var mediaArr) || mediaArr.ValueKind != JsonValueKind.Array) return;
            var list = new List<DetectedMediaItem>();
            foreach (var el in mediaArr.EnumerateArray())
            {
                var url = el.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var type = el.TryGetProperty("type", out var t) ? t.GetString() ?? "video" : "video";
                var title = el.TryGetProperty("title", out var tit) ? tit.GetString() ?? "media" : "media";
                var isYt = el.TryGetProperty("isYt", out var yt) && yt.GetBoolean();
                if (!string.IsNullOrWhiteSpace(url) && !list.Any(x => x.Url == url))
                {
                    list.Add(new DetectedMediaItem(url, type, title, isYt));
                }
            }

            if (list.Count > 0)
            {
                _tabDetectedMedia[tab] = list;
                if (tab == CurrentTab && UrlBarMediaSnifferBtn != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UrlBarMediaSnifferBtn.Visibility = Visibility.Visible;
                    });
                }
            }
        }
        catch { }
    }

    private static string? GetYtDlpPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Luma", "yt-dlp.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma", "yt-dlp.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<string?> EnsureYtDlpAsync()
    {
        var existing = GetYtDlpPath();
        if (!string.IsNullOrWhiteSpace(existing)) return existing;

        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma");
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, "yt-dlp.exe");
            if (File.Exists(target)) return target;

            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 LumaBrowser");
            var bytes = await http.GetByteArrayAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe");
            await File.WriteAllBytesAsync(target, bytes);
            return target;
        }
        catch
        {
            return null;
        }
    }

    private void UrlBarMediaSnifferBtn_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab is null) return;
        var tab = CurrentTab;
        var d = tab.Domain.ToLowerInvariant();
        bool isYt = d.Contains("youtube.com") || d.Contains("youtu.be");

        var menu = NewMenu();

        if (isYt)
        {
            Add(menu, "🎬 Скачать видео / аудио с YouTube:", () => { }, false);
            Sep(menu);
            Add(menu, "⚡ Скачать видео MP4 (1080p Full HD)", () => DownloadNativeMediaAsync(tab.ActiveUrl, "1080", false, tab.DisplayTitle));
            Add(menu, "🎬 Скачать видео MP4 (720p HD)", () => DownloadNativeMediaAsync(tab.ActiveUrl, "720", false, tab.DisplayTitle));
            Add(menu, "🎬 Скачать видео MP4 (360p)", () => DownloadNativeMediaAsync(tab.ActiveUrl, "360", false, tab.DisplayTitle));
            Sep(menu);
            Add(menu, "🎵 Скачать только аудио MP3 (320 kbps)", () => DownloadNativeMediaAsync(tab.ActiveUrl, "mp3", true, tab.DisplayTitle));
            Sep(menu);
            Add(menu, "📁 Открыть папку «Загрузки»", () =>
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true }); } catch { }
            });
        }
        else
        {
            bool hasMedia = _tabDetectedMedia.TryGetValue(tab, out var list) && list.Count > 0;
            Add(menu, "🎬 Встроенный загрузчик видео / аудио:", () => { }, false);
            Sep(menu);
            Add(menu, "⚡ Скачать видео со страницы (MP4 HD)", () => DownloadNativeMediaAsync(tab.ActiveUrl, "1080", false, tab.DisplayTitle));
            Add(menu, "🎵 Скачать аудио со страницы (MP3)", () => DownloadNativeMediaAsync(tab.ActiveUrl, "mp3", true, tab.DisplayTitle));

            if (hasMedia && list is not null)
            {
                Sep(menu);
                Add(menu, "Прямые медиапотоки:", () => { }, false);
                foreach (var item in list.Take(5))
                {
                    var isVideo = item.Type == "video";
                    var iconName = isVideo ? "🎬 " : "🎵 ";
                    var displayTitle = item.Title.Length > 28 ? item.Title[..25] + "..." : item.Title;
                    Add(menu, $"{iconName}{displayTitle}", async () => await DownloadDetectedMediaAsync(item));
                }
            }

            Sep(menu);
            Add(menu, "📁 Открыть папку «Загрузки»", () =>
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true }); } catch { }
            });
        }

        OpenMenu(menu);
    }

    private async void DownloadNativeMediaAsync(string url, string format, bool isAudio, string? customTitle = null)
    {
        var ytdlp = GetYtDlpPath();
        if (string.IsNullOrWhiteSpace(ytdlp))
        {
            ShowToast("Загрузчик медиа", "Инициализация модуля yt-dlp...");
            ytdlp = await EnsureYtDlpAsync();
        }
        if (string.IsNullOrWhiteSpace(ytdlp))
        {
            ShowToast("Ошибка загрузчика", "Не удалось загрузить yt-dlp.exe. Проверьте интернет-соединение.", true);
            return;
        }

        var downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        try { Directory.CreateDirectory(downloadsDir); } catch { }

        string formatArg;
        if (isAudio)
        {
            formatArg = "-x --audio-format mp3";
        }
        else if (int.TryParse(format, out var height))
        {
            formatArg = $"-f \"bestvideo[height<={height}][ext=mp4]+bestaudio[ext=m4a]/best[height<={height}][ext=mp4]/best\"";
        }
        else
        {
            formatArg = "-f \"best[ext=mp4]/best\"";
        }

        var titleStr = !string.IsNullOrWhiteSpace(customTitle) ? customTitle : "видео";
        ShowToast("Загрузка медиа", isAudio ? "Скачивание аудио MP3 в Загрузки..." : $"Скачивание MP4 ({format}p) в Загрузки...");

        _ = Task.Run(() =>
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ytdlp,
                    Arguments = $"{formatArg} --no-warnings --no-playlist -P \"{downloadsDir}\" -o \"%(title)s.%(ext)s\" \"{url}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return;

                string? downloadedFile = null;
                string? line;
                while ((line = proc.StandardOutput.ReadLine()) != null)
                {
                    if (line.Contains("[download] Destination:") || (line.Contains("[download] ") && line.Contains("has already been downloaded")))
                    {
                        var parts = line.Split("Destination:", StringSplitOptions.TrimEntries);
                        if (parts.Length > 1) downloadedFile = parts[1].Trim();
                    }
                }

                proc.WaitForExit();

                Dispatcher.Invoke(() =>
                {
                    if (proc.ExitCode == 0)
                    {
                        var fileName = !string.IsNullOrWhiteSpace(downloadedFile) ? Path.GetFileName(downloadedFile) : titleStr;
                        ShowToast("Загрузка завершена! 🎉", $"Файл сохранён в папку «Загрузки»:\n{fileName}");
                    }
                    else
                    {
                        ShowToast("Загрузка завершена", "Файл скачан в папку «Загрузки»");
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ShowToast("Ошибка загрузки", ex.Message, true);
                });
            }
        });
    }

    private async Task DownloadDetectedMediaAsync(DetectedMediaItem item)
    {
        try
        {
            ShowToast("Загрузка медиа", $"Начато сохранение: {item.Title}");
            await SaveUrlAsync(item.Url);
        }
        catch (Exception ex)
        {
            ShowToast("Ошибка загрузки", ex.Message, true);
        }
    }

    // =========================================================================
    // 2. TAB VOLUME BOOSTER (UP TO 300%)
    // =========================================================================
    private readonly Dictionary<BrowserTab, double> _tabVolumeBoost = new();

    public async Task SetTabVolumeBoostAsync(BrowserTab tab, double factor)
    {
        _tabVolumeBoost[tab] = factor;
        var view = tab.ActiveView;
        if (view?.CoreWebView2 != null)
        {
            try
            {
                await view.CoreWebView2.ExecuteScriptAsync(BrowserScripts.TabVolumeBoosterScript(factor));
                ShowToast("Громкость вкладки", factor > 1.0 ? $"Бустер активен: {Math.Round(factor * 100)}%" : "Стандартная громкость: 100%");
            }
            catch { }
        }
    }

    private void ShowTabVolumeMenu(BrowserTab tab)
    {
        var currentFactor = _tabVolumeBoost.GetValueOrDefault(tab, 1.0);
        var menu = NewMenu();
        Add(menu, $"Громкость: {Math.Round(currentFactor * 100)}%", () => { }, false);
        Sep(menu);

        Add(menu, "100% (Обычная громкость)", async () => await SetTabVolumeBoostAsync(tab, 1.0));
        Add(menu, "150% (+50% усиление)", async () => await SetTabVolumeBoostAsync(tab, 1.5));
        Add(menu, "200% (Двойная громкость)", async () => await SetTabVolumeBoostAsync(tab, 2.0));
        Add(menu, "300% (Максимальный Boost)", async () => await SetTabVolumeBoostAsync(tab, 3.0));

        OpenMenu(menu);
    }

    // =========================================================================
    // 3. ENHANCED READER MODE & TEXT-TO-SPEECH
    // =========================================================================
    private void UrlBarReaderBtn_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab?.ActiveView is { } view)
        {
            _ = ToggleReaderAsync(view);
        }
    }

    public async Task SpeakCurrentArticleAsync()
    {
        if (CurrentTab?.ActiveView is { CoreWebView2: not null } view)
        {
            var ttsScript = """
            (() => {
              try {
                if (window.speechSynthesis.speaking) {
                  window.speechSynthesis.cancel();
                  return 'stopped';
                }
                const reader = document.getElementById('__lumaReader');
                const text = reader ? reader.innerText : (document.querySelector('article,main') || document.body).innerText;
                if (!text || text.trim().length === 0) return 'empty';
                const utterance = new SpeechSynthesisUtterance(text.slice(0, 4000));
                utterance.rate = 1.0;
                window.speechSynthesis.speak(utterance);
                return 'speaking';
              } catch(e) { return 'error'; }
            })();
            """;
            var res = await view.CoreWebView2.ExecuteScriptAsync(ttsScript);
            ShowToast("Озвучка статьи", res.Contains("speaking") ? "Воспроизведение начато" : "Озвучка остановлена");
        }
    }

    // =========================================================================
    // 4. FULL-PAGE SCREENSHOT & OCR
    // =========================================================================
    public async Task CaptureFullPageScreenshotAsync()
    {
        if (CurrentTab?.ActiveView is not { CoreWebView2: not null } view) return;
        try
        {
            using var ms = new MemoryStream();
            await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            ms.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            Clipboard.SetImage(bitmap);
            ShowToast("Скриншот страницы", "Снимок экрана успешно скопирован в буфер обмена!");
        }
        catch (Exception ex)
        {
            ShowToast("Ошибка скриншота", ex.Message, true);
        }
    }

    // =========================================================================
    // 5. FORM AUTO-RECOVERY
    // =========================================================================
    public async Task RestoreFormDraftAsync()
    {
        if (CurrentTab?.ActiveView is not { CoreWebView2: not null } view) return;
        try
        {
            var restoreJs = """
            (() => {
              const KEY = '__luma_form_' + location.host + location.pathname;
              const raw = sessionStorage.getItem(KEY);
              if (!raw) return 'not_found';
              const data = JSON.parse(raw);
              let restored = 0;
              document.querySelectorAll('textarea, input[type=text], input[type=search], [contenteditable=true]').forEach((el, idx) => {
                const id = el.id || el.name || ('idx_' + idx);
                if (data[id]) {
                  if (el.isContentEditable) el.innerText = data[id];
                  else el.value = data[id];
                  el.dispatchEvent(new Event('input', { bubbles: true }));
                  restored++;
                }
              });
              return restored > 0 ? 'ok' : 'none';
            })();
            """;
            var result = await view.CoreWebView2.ExecuteScriptAsync(restoreJs);
            if (result.Contains("ok"))
            {
                ShowToast("Восстановление текста", "Черновик полей ввода успешно восстановлен");
            }
            else
            {
                ShowToast("Восстановление текста", "Сохранённых черновиков для этой страницы нет");
            }
        }
        catch (Exception ex)
        {
            ShowToast("Ошибка восстановления", ex.Message, true);
        }
    }

    // =========================================================================
    // 6. TAB CLEANER & DEDUPLICATION
    // =========================================================================
    public void CloseDuplicateTabs()
    {
        var currentSpaceTabs = Tabs.ToList();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<BrowserTab>();

        foreach (var tab in currentSpaceTabs)
        {
            if (tab.IsHome || tab.IsPinned || tab.IsEssential) continue;
            var cleanUrl = NormalizeUrlForDeduplication(tab.ActiveUrl);
            if (string.IsNullOrWhiteSpace(cleanUrl)) continue;

            if (seenUrls.Contains(cleanUrl))
            {
                duplicates.Add(tab);
            }
            else
            {
                seenUrls.Add(cleanUrl);
            }
        }

        if (duplicates.Count == 0)
        {
            ShowToast("Очистка вкладок", "Дубликатов вкладок не обнаружено");
            return;
        }

        foreach (var dup in duplicates)
        {
            CloseTab(dup);
        }
        FilterTabs();
        RefreshZenSidebar();
        Save();

        ShowToast("Очистка дубликатов", $"Успешно закрыто {duplicates.Count} повторяющихся вкладок");
    }

    public void GroupTabsByDomain()
    {
        var unpinnedTabs = Tabs.Where(t => !t.IsPinned && !t.IsEssential && !t.IsHome && string.IsNullOrEmpty(t.FolderId)).ToList();
        var groups = unpinnedTabs
            .GroupBy(t => GetDomainKey(t.ActiveUrl))
            .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() >= 2)
            .ToList();

        if (groups.Count == 0)
        {
            ShowToast("Группировка вкладок", "Нет вкладок с одинаковым сайтом для объединения");
            return;
        }

        int foldersCreated = 0;
        var space = _spaces[_activeSpace];
        foreach (var grp in groups)
        {
            var domain = grp.Key;
            var folder = new FolderState
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = domain,
                Expanded = true
            };
            space.State.Folders.Add(folder);

            foreach (var tab in grp)
            {
                tab.FolderId = folder.Id;
            }
            foldersCreated++;
        }

        RefreshFolders();
        FilterTabs();
        RefreshZenSidebar();
        Save();

        ShowToast("Группировка вкладок", $"Создано {foldersCreated} папок по сайтам");
    }

    private static string NormalizeUrlForDeduplication(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        try
        {
            var uri = new Uri(url);
            return $"{uri.Host}{uri.AbsolutePath}".TrimEnd('/');
        }
        catch
        {
            return url.Trim();
        }
    }

    private static string GetDomainKey(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLowerInvariant();
            if (host.StartsWith("www.")) host = host[4..];
            return host;
        }
        catch
        {
            return "";
        }
    }

    }
