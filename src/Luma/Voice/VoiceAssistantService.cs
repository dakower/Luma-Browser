using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Luma.Voice;

public sealed class VoiceAssistantService : IDisposable
{
    private readonly MainWindow _mainWindow;
    private readonly WebView2? _speechView;
    private VoiceHudWindow? _hud;
    private bool _isDisposed;
    private bool _isListening;
    public bool IsListening => _isListening;
    private CancellationTokenSource? _activeQueryCts;

    // Win32 Hotkey (Ctrl + Alt + L)
    private const int HotkeyId = 9482;
    private const uint ModCtrl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VkL = 0x4C; // 'L' key
    private HwndSource? _hwndSource;

    private static readonly Regex WakeRegex = new(
        @"(?:^|[\s,.:;!?])(?:(?:эй|хей|hey|ок|окей|слушай|привет)\s+)?(?:люм[а-яё]*|лима|лиму|лимо|лума|луму|лем[а-я]*|дюма|юма|рюма|luma|lima|looma|lume|lema)(?=$|[\s,.:;!?])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WakeOnlyRegex = new(
        @"^\s*(?:(?:эй|хей|hey|ок|окей|слушай|привет)\s+)?(?:люм[а-яё]*|лима|лиму|лимо|лума|луму|лем[а-я]*|дюма|юма|рюма|luma|lima|looma|lume|lema)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public VoiceAssistantService(MainWindow mainWindow, WebView2? speechView = null)
    {
        _mainWindow = mainWindow;
        _speechView = speechView;
    }

    public async void Initialize()
    {
        try
        {
            _hud = new VoiceHudWindow();
            InitGlobalHotkey();
            await InitSpeechEngineAsync();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private void InitGlobalHotkey()
    {
        try
        {
            var handle = new WindowInteropHelper(_mainWindow).Handle;
            if (handle != IntPtr.Zero)
            {
                _hwndSource = HwndSource.FromHwnd(handle);
                _hwndSource?.AddHook(HwndHook);
                RegisterHotKey(handle, HotkeyId, ModCtrl | ModAlt, VkL);
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmHotkey = 0x0312;
        if (msg == wmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _mainWindow.Dispatcher.BeginInvoke(OnHotkeyTriggered);
        }
        return IntPtr.Zero;
    }

    public void OnHotkeyTriggered()
    {
        if (_hud == null) return;
        if (_hud.IsVisible)
        {
            _hud.HideHud();
        }
        else
        {
            _hud.ShowListening("Слушаю команду...");
            try
            {
                _speechView?.CoreWebView2?.ExecuteScriptAsync("window.__startListening && window.__startListening();");
            }
            catch { }
        }
    }

    private async Task InitSpeechEngineAsync()
    {
        try
        {
            if (_speechView == null) return;

            var env = await MainWindow.BrowserEnvironmentAsync();
            await _speechView.EnsureCoreWebView2Async(env);

            _speechView.CoreWebView2.PermissionRequested += (s, e) =>
            {
                if (e.PermissionKind == CoreWebView2PermissionKind.Microphone)
                    e.State = CoreWebView2PermissionState.Allow;
            };

            var voiceDir = Path.Combine(App.ProfilePath, "Voice");
            Directory.CreateDirectory(voiceDir);
            var htmlPath = Path.Combine(voiceDir, "index.html");

            var sysLang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            var stateLang = _mainWindow.State?.Language?.ToLowerInvariant();

            string lang;
            if (stateLang == "uk" || (stateLang != "en" && sysLang == "uk"))
            {
                lang = "uk-UA";
            }
            else if (stateLang == "en" && sysLang != "ru" && sysLang != "uk" && sysLang != "be" && sysLang != "kk")
            {
                lang = "en-US";
            }
            else
            {
                lang = "ru-RU";
            }

            var htmlContent = GetVoiceWorkerHtml(lang);
            await File.WriteAllTextAsync(htmlPath, htmlContent);

            _speechView.CoreWebView2.SetVirtualHostNameToFolderMapping("voice.luma.local", voiceDir, CoreWebView2HostResourceAccessKind.Allow);
            _speechView.CoreWebView2.WebMessageReceived += SpeechView_WebMessageReceived;
            _speechView.CoreWebView2.Navigate("https://voice.luma.local/index.html");

            _isListening = true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    public static string CleanVoiceCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "";
        var cleaned = WakeRegex.Replace(command.Trim(), "");
        return cleaned.Trim().TrimStart(',', ':', '-', ' ');
    }

    public static bool IsWakeWordOnly(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var trimmed = text.Trim().TrimEnd('.', '!', '?', ',');
        return WakeOnlyRegex.IsMatch(trimmed);
    }

    private void SpeechView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var kind = root.TryGetProperty("kind", out var kv) ? kv.GetString() : null;

            if (kind == "speech-result")
            {
                var transcript = root.TryGetProperty("transcript", out var tv) ? tv.GetString() ?? "" : "";
                var cleanCommand = root.TryGetProperty("cleanCommand", out var cv) ? cv.GetString() ?? "" : "";
                var hasWakeWord = root.TryGetProperty("hasWakeWord", out var wv) && wv.GetBoolean();
                var isFinal = root.TryGetProperty("isFinal", out var fv) && fv.GetBoolean();

                var isHudVisible = _hud?.IsVisible == true;

                if (hasWakeWord || isHudVisible)
                {
                    var rawTarget = hasWakeWord && !string.IsNullOrWhiteSpace(cleanCommand) ? cleanCommand : transcript;
                    var command = CleanVoiceCommand(rawTarget);

                    // If it's just the wake word alone (e.g. "Люма!" / "Лима!"), show listening prompt
                    if (IsWakeWordOnly(command))
                    {
                        _mainWindow.Dispatcher.Invoke(() =>
                        {
                            _hud?.ShowListening("Слушаю вас...");
                        });
                        return;
                    }

                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        if (_hud == null) return;
                        if (!_hud.IsVisible)
                        {
                            _hud.ShowListening();
                        }

                        if (!isFinal)
                        {
                            _hud.SetQuery(command);
                        }
                        else
                        {
                            HandleVoiceCommand(command);
                        }
                    });
                }
            }
            else if (kind == "speech-error")
            {
                var error = root.TryGetProperty("error", out var ev) ? ev.GetString() ?? "" : "";
                if (error == "not-allowed")
                {
                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        if (_hud?.IsVisible == true)
                            _hud.ShowError("Микрофон заблокирован. Разрешите доступ к микрофону в Windows.");
                    });
                }
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private async Task<bool> TryExecuteFastActionAsync(string rawCommand)
    {
        var cmd = rawCommand.Trim().ToLowerInvariant().TrimEnd('.', '!', '?', ',');

        // Popular services
        if (Regex.IsMatch(cmd, @"^(?:открой|включи|запусти|перейди на|перейди в)?\s*(?:ютуб|youtube|ютюб)\b"))
        {
            await _mainWindow.AddTabAsync("https://www.youtube.com");
            _hud?.CompleteResponse("Открываю YouTube.");
            _mainWindow.Activate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:открой|включи|запусти|перейди на|перейди в)?\s*(?:гугл|google)\b"))
        {
            await _mainWindow.AddTabAsync("https://www.google.com");
            _hud?.CompleteResponse("Открываю Google.");
            _mainWindow.Activate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:открой|включи|запусти|перейди на|перейди в)?\s*(?:яндекс|ya\.ru)\b"))
        {
            await _mainWindow.AddTabAsync("https://ya.ru");
            _hud?.CompleteResponse("Открываю Яндекс.");
            _mainWindow.Activate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:открой|включи|запусти|перейди на|перейди в)?\s*(?:вк|вконтакте|vk)\b"))
        {
            await _mainWindow.AddTabAsync("https://vk.com");
            _hud?.CompleteResponse("Открываю ВКонтакте.");
            _mainWindow.Activate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:открой|включи|запусти|перейди на|перейди в)?\s*(?:телеграм|телега|телеграмм|telegram)\b"))
        {
            await _mainWindow.AddTabAsync("https://web.telegram.org");
            _hud?.CompleteResponse("Открываю Telegram.");
            _mainWindow.Activate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:открой|включи|запусти|перейди на|перейди в)?\s*(?:кинопоиск)\b"))
        {
            await _mainWindow.AddTabAsync("https://www.kinopoisk.ru");
            _hud?.CompleteResponse("Открываю Кинопоиск.");
            _mainWindow.Activate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:открой|включи|запусти|перейди на|перейди в)?\s*(?:википедию|википедия|wikipedia)\b"))
        {
            await _mainWindow.AddTabAsync("https://ru.wikipedia.org");
            _hud?.CompleteResponse("Открываю Википедию.");
            _mainWindow.Activate();
            return true;
        }

        // Tab and browser navigation
        if (Regex.IsMatch(cmd, @"^(?:новая вкладка|открой новую вкладку|создай вкладку)\b"))
        {
            _mainWindow.OpenNewHomeTab();
            _hud?.CompleteResponse("Новая вкладка открыта.");
            _mainWindow.Activate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:закрой вкладку|закрой эту вкладку|закрой текущую вкладку)\b"))
        {
            if (_mainWindow.CurrentTab != null) _mainWindow.CloseTab(_mainWindow.CurrentTab);
            _hud?.CompleteResponse("Вкладка закрыта.");
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:обнови страницу|обнови|перезагрузи|перезагрузи страницу)\b"))
        {
            _mainWindow.CurrentTab?.ActiveView?.Reload();
            _hud?.CompleteResponse("Страница обновлена.");
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:история|открой историю|покажи историю)\b"))
        {
            _mainWindow.OpenHistory();
            _hud?.CompleteResponse("История открыта.");
            _mainWindow.Activate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:настройки|открой настройки)\b"))
        {
            await _mainWindow.AddTabAsync("luma://settings");
            _hud?.CompleteResponse("Настройки открыты.");
            _mainWindow.Activate();
            return true;
        }

        return false;
    }

    public async void HandleVoiceCommand(string command)
    {
        if (_hud == null || string.IsNullOrWhiteSpace(command)) return;

        _hud.ShowListening();
        _hud.SetQuery(command);

        _activeQueryCts?.Cancel();
        _activeQueryCts = new CancellationTokenSource();
        var ct = _activeQueryCts.Token;

        try
        {
            // Fast direct actions (instant)
            if (await TryExecuteFastActionAsync(command))
            {
                return;
            }

            // If the query asks to play/open something specific (e.g. "включи мне реинкарнацию безработного", "открой ютуб")
            // Note: queries like "найди..." are information questions, NOT navigation commands!
            var isExplicitMediaOpen = Regex.IsMatch(command, @"^(?:включи|поставь|запусти|открой|play|open)\s+", RegexOptions.IgnoreCase);
            if (isExplicitMediaOpen)
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    if (_mainWindow.WindowState == WindowState.Minimized)
                        _mainWindow.WindowState = WindowState.Normal;
                    _mainWindow.Activate();
                });

                // Let the browser assistant resolve and open it
                await _mainWindow.AskAssistantDirectAsync(command, onDelta: delta =>
                {
                    _hud.AppendResponseDelta(delta);
                }, ct);

                _hud.CompleteResponse("Выполняю запрос в браузере Luma.", () =>
                {
                    _mainWindow.Activate();
                });
                return;
            }

            // General Q&A query ("что такое привет", "объясни...", "переведи...")
            var fullAnswer = "";
            await _mainWindow.AskAssistantDirectAsync(command, onDelta: delta =>
            {
                fullAnswer += delta;
                _hud.AppendResponseDelta(delta);
            }, ct);

            _hud.CompleteResponse(fullAnswer, () =>
            {
                _mainWindow.Activate();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.Log(ex);
            _hud.ShowError("Ошибка: " + ex.Message);
        }
    }

    private static string GetVoiceWorkerHtml(string lang = "ru-RU")
    {
        return $$"""
<!DOCTYPE html>
<html>
<head><meta charset="utf-8"><title>Luma Voice</title></head>
<body>
<script>
(function() {
  const SpeechRec = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!SpeechRec) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage({ kind: 'speech-unsupported' });
    }
    return;
  }

  // Wake word regex matching variants of "Люма" / "Luma" (including common misrecognitions like "Лима", "Лума", "Дюма", "Юма")
  const WAKE_WORD_REGEX = /(?:^|[\s,.:;!?])(?:(?:эй|хей|hey|ок|окей|слушай|привет)\s+)?(?:люм[а-яё]*|лима|лиму|лимо|лума|луму|лем[а-я]*|дюма|юма|рюма|luma|lima|looma|lume|lema)(?=$|[\s,.:;!?])/iu;
  const LEADING_WAKE_REGEX = /^\s*(?:(?:эй|хей|hey|ок|окей|слушай|привет)\s+)?(?:люм[а-яё]*|лима|лиму|лимо|лума|луму|лем[а-я]*|дюма|юма|рюма|luma|lima|looma|lume|lema)\s*[,.:;!?]?\s*/iu;

  let recognition = null;
  let restartTimeout = null;

  function normalizeSpokenText(text) {
    if (!text) return '';
    let s = text.trim();
    // Normalize phonetic errors in wake words
    s = s.replace(/(?:^|[\s,.:;!?])(лима|лиму|лимо|лума|луму|лема|лемма|дюма|юма|рюма)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'Люма'));
    // Common site and browser speech corrections
    s = s.replace(/(?:^|[\s,.:;!?])(ютуп|ютубе|ютьюб|youtube)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'ютуб'));
    s = s.replace(/(?:^|[\s,.:;!?])(гугле|google)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'гугл'));
    s = s.replace(/(?:^|[\s,.:;!?])(яндэкс|yandex)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'яндекс'));
    s = s.replace(/(?:^|[\s,.:;!?])(в контакте|vkontakte)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'вконтакте'));
    s = s.replace(/(?:^|[\s,.:;!?])(телегу|телеграмм|telegram)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'телеграм'));
    s = s.replace(/(?:^|[\s,.:;!?])(дискорт|дискорд|discord)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'дискорд'));
    s = s.replace(/(?:^|[\s,.:;!?])(тик ток|тиктоке|tiktok)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'тикток'));
    return s;
  }

  function createRecognition() {
    const rec = new SpeechRec();
    rec.continuous = true;
    rec.interimResults = true;
    rec.maxAlternatives = 5;
    rec.lang = '{{lang}}';

    rec.onstart = () => {
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({ kind: 'speech-started' });
      }
    };

    rec.onresult = (event) => {
      for (let i = event.resultIndex; i < event.results.length; ++i) {
        const res = event.results[i];
        if (!res || res.length === 0) continue;

        // Check all alternatives for wake-word presence
        let bestTranscript = (res[0].transcript || '').trim();
        let hasWakeWord = false;

        for (let a = 0; a < res.length; a++) {
          const altText = (res[a].transcript || '').trim();
          if (WAKE_WORD_REGEX.test(altText)) {
            hasWakeWord = true;
            bestTranscript = altText;
            break;
          }
        }

        if (!hasWakeWord && WAKE_WORD_REGEX.test(bestTranscript)) {
          hasWakeWord = true;
        }

        let cleanCommand = '';
        if (hasWakeWord) {
          cleanCommand = bestTranscript.replace(LEADING_WAKE_REGEX, '').trim();
          cleanCommand = normalizeSpokenText(cleanCommand);
        }

        const normalizedTranscript = normalizeSpokenText(bestTranscript);
        const isFinal = Boolean(res.isFinal);

        if (window.chrome && window.chrome.webview) {
          window.chrome.webview.postMessage({
            kind: 'speech-result',
            transcript: normalizedTranscript,
            cleanCommand: cleanCommand,
            hasWakeWord: hasWakeWord,
            isFinal: isFinal
          });
        }
      }
    };

    rec.onerror = (event) => {
      const err = event.error;
      if (err === 'no-speech' || err === 'network' || err === 'aborted') {
        scheduleRestart(200);
      } else {
        if (window.chrome && window.chrome.webview) {
          window.chrome.webview.postMessage({ kind: 'speech-error', error: err });
        }
        scheduleRestart(1000);
      }
    };

    rec.onend = () => {
      scheduleRestart(150);
    };

    return rec;
  }

  function scheduleRestart(delay) {
    if (restartTimeout) clearTimeout(restartTimeout);
    restartTimeout = setTimeout(() => {
      start();
    }, delay);
  }

  function start() {
    try {
      if (!recognition) recognition = createRecognition();
      recognition.start();
    } catch (e) {
      try {
        if (recognition) recognition.abort();
      } catch (ex) {}
      recognition = null;
      scheduleRestart(300);
    }
  }

  window.__startListening = () => {
    start();
  };

  start();
})();
</script>
</body>
</html>
""";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            var handle = new WindowInteropHelper(_mainWindow).Handle;
            if (handle != IntPtr.Zero)
            {
                UnregisterHotKey(handle, HotkeyId);
            }
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(HwndHook);
                _hwndSource = null;
            }

            _hud?.Close();
            _hud = null;
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}
