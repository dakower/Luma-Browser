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
    private string _activeMicrophoneName = "";
    public string ActiveMicrophoneName => _activeMicrophoneName;
    private bool _isAwaitingVoiceQuery;

    // Win32 Hotkey (Ctrl + Alt + L)
    private const int HotkeyId = 9482;
    private const uint ModCtrl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VkL = 0x4C; // 'L' key
    private HwndSource? _hwndSource;

    private static readonly Regex WakeRegex = new(
        @"(?:^|[\s,.:;!?])(?:(?:эй|хей|hey|ок|окей|слушай|привет|скажи)\s+)?(?:люма|люму|люме|люмой|люмочка|люба|любу|любе|лима|лиму|лума|луму|юма|дюма|luma|looma)(?=$|[\s,.:;!?])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WakeOnlyRegex = new(
        @"^\s*(?:(?:эй|хей|hey|ок|окей|слушай|привет|скажи)\s+)?(?:люма|люму|люме|люмой|люмочка|люба|любу|любе|лима|лиму|лума|луму|юма|дюма|luma|looma)?\s*$",
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
            _hud.OnDismissed += () => { _isAwaitingVoiceQuery = false; };
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
            _isAwaitingVoiceQuery = false;
            _hud.HideHud();
        }
        else
        {
            _isAwaitingVoiceQuery = true;
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

            var prefMicId = _mainWindow.State?.VoiceMicrophoneId ?? "";
            var prefMicName = _mainWindow.State?.VoiceMicrophoneName ?? "";
            var htmlContent = GetVoiceWorkerHtml(lang, prefMicId, prefMicName);
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

    public static string ExtractCommandAfterWake(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var matches = WakeRegex.Matches(text);
        if (matches.Count > 0)
        {
            var last = matches[^1];
            var after = text.Substring(last.Index + last.Length).TrimStart(' ', ',', ':', ';', '!', '?', '-');
            return after.Trim();
        }
        return text.Trim();
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

            if (kind == "active-microphone")
            {
                var label = root.TryGetProperty("label", out var lv) ? lv.GetString() ?? "" : "";
                var devId = root.TryGetProperty("deviceId", out var dv) ? dv.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(label))
                {
                    _activeMicrophoneName = label;
                    if (_mainWindow.State != null)
                    {
                        _mainWindow.State.VoiceMicrophoneName = label;
                        _mainWindow.State.VoiceMicrophoneId = devId;
                        _mainWindow.SaveState();
                    }
                }
                return;
            }

            if (kind == "speech-result")
            {
                var transcript = root.TryGetProperty("transcript", out var tv) ? tv.GetString() ?? "" : "";
                var cleanCommand = root.TryGetProperty("cleanCommand", out var cv) ? cv.GetString() ?? "" : "";
                var hasWakeWord = root.TryGetProperty("hasWakeWord", out var wv) && wv.GetBoolean();
                var isFinal = root.TryGetProperty("isFinal", out var fv) && fv.GetBoolean();

                var isHudAwaiting = _hud?.IsVisible == true && _hud.IsAwaitingQuery;
                var isAwaiting = isHudAwaiting || _isAwaitingVoiceQuery;

                if (hasWakeWord)
                {
                    // Discard everything spoken before the wake word (game chat, Discord talks, background chatter)
                    var rawTarget = !string.IsNullOrWhiteSpace(cleanCommand) ? cleanCommand : transcript;
                    var command = ExtractCommandAfterWake(rawTarget);
                    command = CleanVoiceCommand(command);

                    // If it's just the wake word alone (e.g. "Люма!" / "Привет Люма!"), show listening prompt
                    if (string.IsNullOrWhiteSpace(command) || IsWakeWordOnly(command))
                    {
                        _isAwaitingVoiceQuery = true;
                        _mainWindow.Dispatcher.Invoke(() =>
                        {
                            if (_hud == null) return;
                            if (!_hud.IsVisible) _hud.ShowListening("Слушаю вас...");
                            else _hud.SetStatus("Слушаю вас...");
                        });
                        return;
                    }

                    _isAwaitingVoiceQuery = true;
                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        if (_hud == null) return;
                        if (!_hud.IsVisible)
                        {
                            _hud.ShowListening("Слушаю вас...");
                        }

                        if (!isFinal)
                        {
                            _hud.SetInterimQuery(command);
                        }
                        else
                        {
                            _isAwaitingVoiceQuery = false;
                            _hud.SetFinalQueryAndThinking(command);
                            HandleVoiceCommand(command);
                        }
                    });
                }
                else if (isAwaiting)
                {
                    // Follow-up speech ONLY when HUD was explicitly waiting for the query
                    var command = CleanVoiceCommand(transcript);
                    if (string.IsNullOrWhiteSpace(command)) return;

                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        if (_hud == null) return;
                        if (!_hud.IsVisible)
                        {
                            _hud.ShowListening("Слушаю вас...");
                        }

                        if (!isFinal)
                        {
                            _hud.SetInterimQuery(command);
                        }
                        else
                        {
                            _isAwaitingVoiceQuery = false;
                            _hud.SetFinalQueryAndThinking(command);
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

        // Popular services - automatically switch window to Luma
        if (Regex.IsMatch(cmd, @"^(?:открой|включи|запусти|перейди на|перейди в)?\s*(?:ютуб|youtube|ютюб)\b"))
        {
            await _mainWindow.AddTabAsync("https://www.youtube.com");
            _hud?.CompleteResponse("Открываю YouTube.", () => _mainWindow.BringToFrontAndActivate());
            _mainWindow.BringToFrontAndActivate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:открой|включи|запусти|перейди на|перейди в)?\s*(?:гугл|google)\b"))
        {
            await _mainWindow.AddTabAsync("https://www.google.com");
            _hud?.CompleteResponse("Открываю Google.", () => _mainWindow.BringToFrontAndActivate());
            _mainWindow.BringToFrontAndActivate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:открой|включи|запусти|перейди на|перейди в)?\s*(?:яндекс|ya\.ru)\b"))
        {
            await _mainWindow.AddTabAsync("https://ya.ru");
            _hud?.CompleteResponse("Открываю Яндекс.", () => _mainWindow.BringToFrontAndActivate());
            _mainWindow.BringToFrontAndActivate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:открой|включи|запусти|перейди на|перейди в)?\s*(?:вк|вконтакте|vk)\b"))
        {
            await _mainWindow.AddTabAsync("https://vk.com");
            _hud?.CompleteResponse("Открываю ВКонтакте.", () => _mainWindow.BringToFrontAndActivate());
            _mainWindow.BringToFrontAndActivate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:открой|включи|запусти|перейди на|перейди в)?\s*(?:телеграм|телега|телеграмм|telegram)\b"))
        {
            await _mainWindow.AddTabAsync("https://web.telegram.org");
            _hud?.CompleteResponse("Открываю Telegram.", () => _mainWindow.BringToFrontAndActivate());
            _mainWindow.BringToFrontAndActivate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:открой|включи|запусти|перейди на|перейди в)?\s*(?:кинопоиск)\b"))
        {
            await _mainWindow.AddTabAsync("https://www.kinopoisk.ru");
            _hud?.CompleteResponse("Открываю Кинопоиск.", () => _mainWindow.BringToFrontAndActivate());
            _mainWindow.BringToFrontAndActivate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:открой|включи|запусти|перейди на|перейди в)?\s*(?:википедию|википедия|wikipedia)\b"))
        {
            await _mainWindow.AddTabAsync("https://ru.wikipedia.org");
            _hud?.CompleteResponse("Открываю Википедию.", () => _mainWindow.BringToFrontAndActivate());
            _mainWindow.BringToFrontAndActivate();
            return true;
        }

        // Tab and browser navigation - automatically switch window to Luma
        if (Regex.IsMatch(cmd, @"^(?:новая вкладка|открой новую вкладку|создай вкладку)\b"))
        {
            _mainWindow.OpenNewHomeTab();
            _hud?.CompleteResponse("Новая вкладка открыта.", () => _mainWindow.BringToFrontAndActivate());
            _mainWindow.BringToFrontAndActivate();
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
            _hud?.CompleteResponse("История открыта.", () => _mainWindow.BringToFrontAndActivate());
            _mainWindow.BringToFrontAndActivate();
            return true;
        }
        if (Regex.IsMatch(cmd, @"^(?:настройки|открой настройки)\b"))
        {
            await _mainWindow.AddTabAsync("luma://settings");
            _hud?.CompleteResponse("Настройки открыты.", () => _mainWindow.BringToFrontAndActivate());
            _mainWindow.BringToFrontAndActivate();
            return true;
        }

        return false;
    }

    public async void HandleVoiceCommand(string command)
    {
        if (_hud == null || string.IsNullOrWhiteSpace(command)) return;

        // Capture user's screen at the exact moment of the question!
        var screenTask = Task.Run(() => DesktopCapture.CaptureScreenBase64());

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

            var screenCapture = await screenTask;

            // Autonomous Browser Agent (e.g. "пройди тест", "реши опрос", "заполни форму", "поставь лайк")
            if (BrowserAgent.BrowserAgentRunner.IsAgentGoal(command))
            {
                _mainWindow.BringToFrontAndActivate();
                var agentAnswer = "";
                await _mainWindow.AskAssistantDirectAsync(command, onDelta: delta =>
                {
                    agentAnswer += delta;
                    _hud.AppendResponseDelta(delta);
                }, ct, screenCapture);

                _hud.CompleteResponse(string.IsNullOrWhiteSpace(agentAnswer) ? "Задача выполнена агентом в браузере." : agentAnswer, () =>
                {
                    _mainWindow.BringToFrontAndActivate();
                });
                _mainWindow.BringToFrontAndActivate();
                return;
            }

            // If the query asks to play/open something specific (e.g. "открой видео такого-то", "включи музыку", "открой ютуб")
            var isExplicitMediaOpen = Regex.IsMatch(command, @"^(?:включи|поставь|запусти|открой|найди в браузере|play|open)\s+", RegexOptions.IgnoreCase);
            if (isExplicitMediaOpen)
            {
                _mainWindow.BringToFrontAndActivate();

                var mediaAnswer = "";
                await _mainWindow.AskAssistantDirectAsync(command, onDelta: delta =>
                {
                    mediaAnswer += delta;
                    _hud.AppendResponseDelta(delta);
                }, ct, screenCapture);

                // Auto-fallback: if the model didn't open anything via %%LUMA%%, but user asked to open a video/media/site
                var videoMatch = Regex.Match(command, @"^(?:открой|включи|поставь|запусти)\s+(?:видео|ролик|клип|стрим|песню|трек|музыку)?\s*(?<q>.+)$", RegexOptions.IgnoreCase);
                if (videoMatch.Success && !mediaAnswer.Contains("%%LUMA%%"))
                {
                    var mediaQuery = videoMatch.Groups["q"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(mediaQuery) && !mediaQuery.Equals("ютуб", StringComparison.OrdinalIgnoreCase))
                    {
                        var isMusic = Regex.IsMatch(command, @"(?:песню|трек|музыку)");
                        var kind = isMusic ? "music" : "video";
                        try
                        {
                            var url = await SiteResolver.ResolveAsync(kind, mediaQuery, ct);
                            var tab = await _mainWindow.AddTabAsync(url);
                            if (SiteResolver.IsSearchPage(url)) _ = _mainWindow.AutoPickResultAsync(tab, mediaQuery);
                        }
                        catch { }
                    }
                }

                _hud.CompleteResponse(string.IsNullOrWhiteSpace(mediaAnswer) ? "Открываю в браузере Luma." : mediaAnswer, () =>
                {
                    _mainWindow.BringToFrontAndActivate();
                });
                _mainWindow.BringToFrontAndActivate();
                return;
            }

            // General Q&A query ("найди...", "что такое...", "что на экране...", "кто это...", "помоги пройти...")
            // Like Gemini: sees user's screen in real-time, searches internet silently in background and answers directly in Voice HUD without opening browser tabs!
            var fullAnswer = "";
            await _mainWindow.AskAssistantDirectAsync(command, onDelta: delta =>
            {
                fullAnswer += delta;
                _hud.AppendResponseDelta(delta);
            }, ct, screenCapture);

            _hud.CompleteResponse(fullAnswer, () =>
            {
                _mainWindow.BringToFrontAndActivate();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.Log(ex);
            _hud.ShowError("Ошибка: " + ex.Message);
        }
    }

    private static string GetVoiceWorkerHtml(string lang = "ru-RU", string preferredMicId = "", string preferredMicName = "")
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

  // Wake-word regex matching "Люма" and common STT phonetic variants
  const WAKE_WORD_REGEX = /(?:^|[\s,.:;!?])(?:(?:эй|хей|hey|ок|окей|слушай|привет|скажи)\s+)?(?:люма|люму|люме|люмой|люмочка|люба|любу|любе|лима|лиму|лума|луму|юма|дюма|luma|looma)(?=$|[\s,.:;!?])/iu;

  let recognition = null;
  let restartTimeout = null;
  let finalizeTimer = null;
  let accumulatedFinal = '';
  let pendingHasWake = false;
  let selectedDeviceId = null;
  let micInitialized = false;

  async function setupMicrophone() {
    try {
      if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) return;

      // Temporary audio stream to unlock device labels and permissions
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        // Stop tracks immediately so the microphone hardware is not locked and SpeechRecognition has unimpeded access!
        stream.getTracks().forEach(t => t.stop());
      } catch (e) {}

      const devices = await navigator.mediaDevices.enumerateDevices();
      const audioInputs = devices.filter(d => d.kind === 'audioinput');

      // Filter out virtual audio loopbacks that route computer / game / video sound
      const isLoopbackOrVirtual = (name) => {
        const s = (name || '').toLowerCase();
        return s.includes('stereo mix') || s.includes('стерео микшер') ||
               s.includes('what u hear') || s.includes('wave out') ||
               s.includes('vb-audio') || s.includes('virtual cable') ||
               s.includes('cable output') || s.includes('voicemeeter') ||
               s.includes('line in') || s.includes('loopback') ||
               s.includes('steam streaming');
      };

      let chosen = null;
      const prefId = '{{preferredMicId}}';
      const prefName = '{{preferredMicName}}'.toLowerCase();

      if (prefId) {
        chosen = audioInputs.find(d => d.deviceId === prefId);
      }
      if (!chosen && prefName) {
        chosen = audioInputs.find(d => (d.label || '').toLowerCase().includes(prefName));
      }

      // Prioritize real dedicated physical microphones (HATOR, USB, Headset, Array)
      if (!chosen) {
        chosen = audioInputs.find(d => !isLoopbackOrVirtual(d.label) && (
          d.label.toLowerCase().includes('hator') ||
          d.label.toLowerCase().includes('usb') ||
          d.label.toLowerCase().includes('signify') ||
          d.label.toLowerCase().includes('headset') ||
          d.label.toLowerCase().includes('микрофон') ||
          d.label.toLowerCase().includes('mic')
        ));
      }

      // Fallback to any non-virtual microphone
      if (!chosen) {
        chosen = audioInputs.find(d => !isLoopbackOrVirtual(d.label));
      }

      if (!chosen && audioInputs.length > 0) {
        chosen = audioInputs[0];
      }

      if (chosen) {
        selectedDeviceId = chosen.deviceId;
        if (window.chrome && window.chrome.webview) {
          window.chrome.webview.postMessage({
            kind: 'active-microphone',
            deviceId: chosen.deviceId,
            label: chosen.label
          });
        }
      }
    } catch (err) {
      console.warn('Microphone setup warning:', err);
    }
  }

  function normalizeSpokenText(text) {
    if (!text) return '';
    let s = text.trim();
    // Normalize wake words
    s = s.replace(/(?:^|[\s,.:;!?])(люма|люму|люме|люмой|люмочка|люба|любу|любе|лима|лиму|лума|луму|юма|дюма|luma|looma)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'Люма'));
    // Common spoken shortcuts & colloquialisms
    s = s.replace(/\bче\s+за\b/giu, 'что за');
    s = s.replace(/\bче\s+это\b/giu, 'что это');
    s = s.replace(/\bче\s+там\b/giu, 'что там');
    s = s.replace(/\bче\b/giu, 'что');
    s = s.replace(/\bскока\b/giu, 'сколько');
    s = s.replace(/\bщас\b|\bща\b/giu, 'сейчас');
    s = s.replace(/\bв\s+инете\b|\bв\s+инте\b/giu, 'в интернете');
    s = s.replace(/\bвруби\b|\bзаруби\b/giu, 'включи');
    s = s.replace(/\bпозырь\b|\bглянь\b/giu, 'посмотри');
    // Popular sites and tech terms
    s = s.replace(/(?:^|[\s,.:;!?])(ютуп|ютубе|ютьюб|youtube)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'ютуб'));
    s = s.replace(/(?:^|[\s,.:;!?])(гугле|google)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'гугл'));
    s = s.replace(/(?:^|[\s,.:;!?])(яндэкс|yandex)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'яндекс'));
    s = s.replace(/(?:^|[\s,.:;!?])(в контакте|vkontakte)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'вконтакте'));
    s = s.replace(/(?:^|[\s,.:;!?])(телегу|телеграмм|telegram)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'телеграм'));
    s = s.replace(/(?:^|[\s,.:;!?])(дискорт|discord)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'дискорд'));
    s = s.replace(/(?:^|[\s,.:;!?])(тик ток|тиктоке|tiktok)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'тикток'));
    s = s.replace(/(?:^|[\s,.:;!?])(стим|steam)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'стим'));
    s = s.replace(/(?:^|[\s,.:;!?])(доту|доте|dota)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'дота'));
    s = s.replace(/(?:^|[\s,.:;!?])(ксочку|ксго|csgo|cs\s*go|cs2)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'кс'));
    s = s.replace(/(?:^|[\s,.:;!?])(майн|minecraft)(?=$|[\s,.:;!?])/giu, (m, g) => m.replace(g, 'майнкрафт'));
    return s;
  }

  function extractCommandAfterWake(text) {
    if (!text) return { hasWake: false, after: '' };
    // Match the LAST wake-word occurrence to handle stuttering ("Люма... эээ Люма, открой")
    let match = null;
    let lastMatch = null;
    const regex = new RegExp(WAKE_WORD_REGEX.source, 'giu');
    while ((match = regex.exec(text)) !== null) {
      lastMatch = match;
    }
    if (!lastMatch) return { hasWake: false, after: '' };
    const after = text.substring(lastMatch.index + lastMatch[0].length).replace(/^[,.:;!?\s]+/, '').trim();
    return { hasWake: true, after: after };
  }

  function flushFinalCommand() {
    if (finalizeTimer) {
      clearTimeout(finalizeTimer);
      finalizeTimer = null;
    }
    const text = accumulatedFinal.trim();
    accumulatedFinal = '';
    const hadWake = pendingHasWake;
    pendingHasWake = false;

    if (!text) return;

    let cleanCommand = '';
    if (hadWake) {
      const extracted = extractCommandAfterWake(text);
      cleanCommand = normalizeSpokenText(extracted.after);
    }
    const norm = normalizeSpokenText(text);

    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage({
        kind: 'speech-result',
        transcript: norm,
        cleanCommand: cleanCommand,
        hasWakeWord: hadWake,
        isFinal: true
      });
    }
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
      if (finalizeTimer) {
        clearTimeout(finalizeTimer);
        finalizeTimer = null;
      }

      let interimChunk = '';

      for (let i = event.resultIndex; i < event.results.length; ++i) {
        const res = event.results[i];
        if (!res || res.length === 0) continue;

        let bestAlt = (res[0].transcript || '').trim();
        let altHasWake = false;

        // Check up to 5 alternatives to find wake-word or best recognized text
        for (let a = 0; a < res.length; a++) {
          const t = (res[a].transcript || '').trim();
          const ext = extractCommandAfterWake(t);
          if (ext.hasWake) {
            altHasWake = true;
            bestAlt = t;
            break;
          }
        }

        if (!altHasWake) {
          const ext = extractCommandAfterWake(bestAlt);
          if (ext.hasWake) {
            altHasWake = true;
          }
        }

        if (altHasWake) {
          pendingHasWake = true;
        }

        if (res.isFinal) {
          if (accumulatedFinal) accumulatedFinal += ' ';
          accumulatedFinal += bestAlt;
        } else {
          interimChunk = bestAlt;
        }
      }

      // Stream interim text to HUD so user sees live feedback immediately
      const currentCombined = ((accumulatedFinal ? accumulatedFinal + ' ' : '') + interimChunk).trim();
      if (currentCombined) {
        let cleanInterim = '';
        if (pendingHasWake) {
          const ext = extractCommandAfterWake(currentCombined);
          cleanInterim = normalizeSpokenText(ext.after);
        }
        const normInterim = normalizeSpokenText(currentCombined);

        if (window.chrome && window.chrome.webview) {
          window.chrome.webview.postMessage({
            kind: 'speech-result',
            transcript: normInterim,
            cleanCommand: cleanInterim,
            hasWakeWord: pendingHasWake,
            isFinal: false
          });
        }
      }

      if (accumulatedFinal.trim()) {
        const delay = interimChunk ? 750 : 450;
        finalizeTimer = setTimeout(flushFinalCommand, delay);
      }
    };

    rec.onerror = (event) => {
      const err = event.error;
      if (err === 'no-speech' || err === 'network' || err === 'aborted') {
        scheduleRestart(150);
      } else {
        if (window.chrome && window.chrome.webview) {
          window.chrome.webview.postMessage({ kind: 'speech-error', error: err });
        }
        scheduleRestart(800);
      }
    };

    rec.onend = () => {
      if (accumulatedFinal.trim()) {
        flushFinalCommand();
      }
      scheduleRestart(100);
    };

    return rec;
  }

  function scheduleRestart(delay) {
    if (restartTimeout) clearTimeout(restartTimeout);
    restartTimeout = setTimeout(() => {
      start();
    }, delay);
  }

  async function start() {
    try {
      if (!micInitialized) {
        micInitialized = true;
        await setupMicrophone();
      }
      if (!recognition) recognition = createRecognition();
      recognition.start();
    } catch (e) {
      try {
        if (recognition) recognition.abort();
      } catch (ex) {}
      recognition = null;
      scheduleRestart(250);
    }
  }

  window.__startListening = () => { start(); };
  window.__setMicrophone = async (deviceId) => {
    selectedDeviceId = deviceId;
    await setupMicrophone();
    if (recognition) {
      try { recognition.abort(); } catch(e) {}
      recognition = null;
    }
    start();
  };

  start();
})();
</script>
</body>
</html>
""";
    }

    public void SwitchMicrophone(string deviceId, string deviceName)
    {
        _activeMicrophoneName = deviceName;
        if (_mainWindow.State != null)
        {
            _mainWindow.State.VoiceMicrophoneId = deviceId;
            _mainWindow.State.VoiceMicrophoneName = deviceName;
            _mainWindow.SaveState();
        }
        try
        {
            _speechView?.CoreWebView2?.ExecuteScriptAsync($"window.__setMicrophone && window.__setMicrophone({System.Text.Json.JsonSerializer.Serialize(deviceId)});");
        }
        catch { }
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
