using System.Globalization;
using System.Runtime.InteropServices;
using System.Speech.Recognition;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;

namespace Luma.Voice;

public sealed class VoiceAssistantService : IDisposable
{
    private readonly MainWindow _mainWindow;
    private SpeechRecognitionEngine? _engine;
    private VoiceHudWindow? _hud;
    private bool _isDisposed;
    private bool _isListening;
    public bool IsListening => _isListening;
    private CancellationTokenSource? _activeQueryCts;

    // Win32 Hotkey
    private const int HotkeyId = 9482;
    private const uint ModCtrl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VkL = 0x4C; // 'L' key
    private HwndSource? _hwndSource;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public VoiceAssistantService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void Initialize()
    {
        try
        {
            _hud = new VoiceHudWindow();
            InitSpeechEngine();
            InitGlobalHotkey();
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

    private void OnHotkeyTriggered()
    {
        if (_hud == null) return;
        if (_hud.IsVisible)
        {
            _hud.HideHud();
        }
        else
        {
            _hud.ShowListening("Слушаю команду...");
        }
    }

    private void InitSpeechEngine()
    {
        try
        {
            // Pick recognizer: try ru-RU first, then en-US, then default installed recognizer
            RecognizerInfo? recognizer = null;
            foreach (var ri in SpeechRecognitionEngine.InstalledRecognizers())
            {
                if (ri.Culture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase) ||
                    ri.Culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
                {
                    recognizer = ri;
                    break;
                }
            }

            _engine = recognizer != null ? new SpeechRecognitionEngine(recognizer) : new SpeechRecognitionEngine();
            _engine.SetInputToDefaultAudioDevice();

            // Wake-word grammar
            var wakeWords = new Choices("Luma", "Hey Luma", "Люма", "Эй Люма", "Хей Люма", "Окей Люма");
            var wakeBuilder = new GrammarBuilder(wakeWords);
            var wakeGrammar = new Grammar(wakeBuilder) { Name = "WakeWord" };
            _engine.LoadGrammar(wakeGrammar);

            // Free speech / Dictation grammar to capture the full command
            var dictationGrammar = new DictationGrammar { Name = "Dictation" };
            _engine.LoadGrammar(dictationGrammar);

            _engine.SpeechRecognized += Engine_SpeechRecognized;
            _engine.RecognizeAsync(RecognizeMode.Multiple);
            _isListening = true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private void Engine_SpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result == null || e.Result.Confidence < 0.28f) return;

        var text = e.Result.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        // Check if wake word is present
        var isWake = Regex.IsMatch(text, @"\b(?:luma|hey luma|люма|эй люма|хей люма|окей люма)\b", RegexOptions.IgnoreCase);
        if (!isWake && !(_hud?.IsVisible ?? false))
        {
            // Neither wake word nor active HUD prompt
            return;
        }

        // Clean out the wake word prefix to get the command
        var cleanCommand = Regex.Replace(text, @"^\s*(?:hey\s+|эй\s+|хей\s+|окей\s+)?(?:luma|люма)\s*[,:]?\s*", "", RegexOptions.IgnoreCase).Trim();

        _mainWindow.Dispatcher.BeginInvoke(() =>
        {
            HandleVoiceCommand(cleanCommand.Length > 0 ? cleanCommand : text);
        });
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
            // If the query asks to play/open something (e.g. "включи мне реинкарнацию безработного", "открой ютуб")
            var isNav = Regex.IsMatch(command, @"\b(?:включи|открой|найди|запусти|поставь|play|open)\b", RegexOptions.IgnoreCase);
            if (isNav)
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

            if (_engine != null)
            {
                _engine.RecognizeAsyncStop();
                _engine.Dispose();
                _engine = null;
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
