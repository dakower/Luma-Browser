using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MediaColor = System.Windows.Media.Color;

namespace Luma.Voice;

public partial class VoiceHudWindow : Window
{
    private const string SoundOnPath = "M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z";
    private const string SoundOffPath = "M16.5 12c0-1.77-1.02-3.29-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51C20.63 14.91 21 13.5 21 12c0-4.28-2.99-7.86-7-8.77v2.06c2.89.86 5 3.54 5 6.71zM4.27 3L3 4.27l4.73 4.73H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06c1.38-.31 2.63-.95 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4L9.91 6.09 12 8.18V4z";

    private readonly DispatcherTimer _autoCloseTimer = new();
    private Storyboard? _pulseAnimation;
    private Storyboard? _waveAnimation;
    private Action? _onOpenBrowser;

    public bool IsMuted { get; private set; }
    public Action<bool>? OnMuteToggled { get; set; }
    public Action? OnHudHiding { get; set; }

    public VoiceHudWindow()
    {
        InitializeComponent();
        _autoCloseTimer.Interval = TimeSpan.FromSeconds(15);
        _autoCloseTimer.Tick += (_, _) => HideHud();
        Loaded += VoiceHudWindow_Loaded;
        PreviewKeyDown += VoiceHudWindow_PreviewKeyDown;
    }

    private void VoiceHudWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
        _pulseAnimation = (Storyboard)Resources["PulseOrb"];
        _waveAnimation = (Storyboard)Resources["WaveVisualizer"];
        _pulseAnimation?.Begin();
    }

    private void VoiceHudWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideHud();
            e.Handled = true;
        }
    }

    public void PositionWindow()
    {
        var area = SystemParameters.WorkArea;
        // Position on top-right with margin
        Left = area.Right - Width - 24;
        Top = area.Top + 36;
    }

    public void SetMuted(bool muted)
    {
        Dispatcher.Invoke(() =>
        {
            IsMuted = muted;
            UpdateMuteUi();
        });
    }

    private void UpdateMuteUi()
    {
        MuteIcon.Data = Geometry.Parse(IsMuted ? SoundOffPath : SoundOnPath);
        MuteIcon.Fill = IsMuted ? new SolidColorBrush(MediaColor.FromRgb(156, 163, 175)) : new SolidColorBrush(MediaColor.FromRgb(196, 181, 253));
        MuteButton.ToolTip = IsMuted ? "Включить озвучку ответов" : "Отключить озвучку ответов";
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        IsMuted = !IsMuted;
        UpdateMuteUi();
        OnMuteToggled?.Invoke(IsMuted);
    }

    public void ShowListening(string statusText = "Слушаю...")
    {
        Dispatcher.Invoke(() =>
        {
            _autoCloseTimer.Stop();
            _autoCloseTimer.Interval = TimeSpan.FromSeconds(15);
            _autoCloseTimer.Start();

            PositionWindow();
            StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(16, 185, 129)); // Emerald Green
            StatusBadge.Text = statusText;
            StatusBadge.Foreground = new SolidColorBrush(MediaColor.FromRgb(167, 243, 208));

            QueryBorder.Visibility = Visibility.Collapsed;
            ResponseText.Text = "";
            ActionBar.Visibility = Visibility.Collapsed;

            _waveAnimation?.Begin();
            _pulseAnimation?.Begin();

            if (!IsVisible) Show();
            Activate();
        });
    }

    public void SetStatus(string statusText)
    {
        Dispatcher.Invoke(() =>
        {
            StatusBadge.Text = statusText;
            _autoCloseTimer.Stop();
            _autoCloseTimer.Interval = TimeSpan.FromSeconds(15);
            _autoCloseTimer.Start();
        });
    }

    public void SetQuery(string query)
    {
        Dispatcher.Invoke(() =>
        {
            _autoCloseTimer.Stop();
            _autoCloseTimer.Interval = TimeSpan.FromSeconds(15);
            _autoCloseTimer.Start();

            QueryText.Text = query;
            QueryBorder.Visibility = Visibility.Visible;
            StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(129, 140, 248)); // Indigo
            StatusBadge.Text = "Думаю...";
            StatusBadge.Foreground = new SolidColorBrush(MediaColor.FromRgb(199, 210, 254));
            _waveAnimation?.Stop();
        });
    }

    public void AppendResponseDelta(string delta)
    {
        Dispatcher.Invoke(() =>
        {
            _autoCloseTimer.Stop();
            _autoCloseTimer.Interval = TimeSpan.FromSeconds(15);
            _autoCloseTimer.Start();

            StatusBadge.Text = "LumaAI отвечает...";
            ResponseText.Text += delta;
        });
    }

    public void CompleteResponse(string fullAnswer, Action? openBrowserAction = null)
    {
        Dispatcher.Invoke(() =>
        {
            _pulseAnimation?.Stop();
            _waveAnimation?.Stop();
            StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(56, 189, 248)); // Sky Blue
            StatusBadge.Text = "Готово";
            StatusBadge.Foreground = new SolidColorBrush(MediaColor.FromRgb(186, 230, 253));

            if (string.IsNullOrWhiteSpace(ResponseText.Text))
                ResponseText.Text = fullAnswer;

            _onOpenBrowser = openBrowserAction;
            ActionBar.Visibility = openBrowserAction != null ? Visibility.Visible : Visibility.Collapsed;

            _autoCloseTimer.Stop();
            _autoCloseTimer.Interval = TimeSpan.FromSeconds(15);
            _autoCloseTimer.Start();
        });
    }

    public void SetSpeakingState(bool isSpeaking)
    {
        Dispatcher.Invoke(() =>
        {
            if (isSpeaking)
            {
                _autoCloseTimer.Stop(); // Do not close HUD while voice is playing!
                StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(236, 72, 153)); // Pink
                StatusBadge.Text = "Говорит...";
                StatusBadge.Foreground = new SolidColorBrush(MediaColor.FromRgb(244, 114, 182));
                _waveAnimation?.Begin();
                _pulseAnimation?.Begin();
            }
            else
            {
                _waveAnimation?.Stop();
                _pulseAnimation?.Stop();
                StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(16, 185, 129));
                StatusBadge.Text = "Готово";
                StatusBadge.Foreground = new SolidColorBrush(MediaColor.FromRgb(167, 243, 208));

                // 15 seconds timer after speech finishes
                _autoCloseTimer.Stop();
                _autoCloseTimer.Interval = TimeSpan.FromSeconds(15);
                _autoCloseTimer.Start();
            }
        });
    }

    public void ShowError(string error)
    {
        Dispatcher.Invoke(() =>
        {
            _pulseAnimation?.Stop();
            _waveAnimation?.Stop();
            StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(239, 68, 68)); // Red
            StatusBadge.Text = "Ошибка";
            StatusBadge.Foreground = new SolidColorBrush(MediaColor.FromRgb(252, 165, 165));
            ResponseText.Text = error;

            _autoCloseTimer.Stop();
            _autoCloseTimer.Interval = TimeSpan.FromSeconds(10);
            _autoCloseTimer.Start();
        });
    }

    public void HideHud()
    {
        Dispatcher.Invoke(() =>
        {
            _autoCloseTimer.Stop();
            _pulseAnimation?.Stop();
            _waveAnimation?.Stop();
            OnHudHiding?.Invoke();
            Hide();
        });
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        HideHud();
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        _onOpenBrowser?.Invoke();
        HideHud();
    }
}
