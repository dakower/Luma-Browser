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
    private readonly DispatcherTimer _autoCloseTimer = new();
    private Storyboard? _pulseAnimation;
    private Action? _onOpenBrowser;

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

    public void ShowListening(string statusText = "Слушаю...")
    {
        Dispatcher.Invoke(() =>
        {
            _autoCloseTimer.Stop();
            _autoCloseTimer.Interval = TimeSpan.FromSeconds(15);
            _autoCloseTimer.Start();

            PositionWindow();
            StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(116, 104, 199)); // #7468C7
            StatusBadge.Text = statusText;
            StatusBadge.Foreground = new SolidColorBrush(MediaColor.FromRgb(209, 210, 214)); // #D1D2D6

            QueryBorder.Visibility = Visibility.Collapsed;
            ResponseText.Text = "";
            ActionBar.Visibility = Visibility.Collapsed;

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
            StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(169, 162, 216)); // #A9A2D8
            StatusBadge.Text = "Думаю...";
            StatusBadge.Foreground = new SolidColorBrush(MediaColor.FromRgb(209, 210, 214));
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
            StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(121, 224, 165)); // #79E0A5 (Soft emerald)
            StatusBadge.Text = "Готово";
            StatusBadge.Foreground = new SolidColorBrush(MediaColor.FromRgb(188, 233, 205));

            if (string.IsNullOrWhiteSpace(ResponseText.Text))
                ResponseText.Text = fullAnswer;

            _onOpenBrowser = openBrowserAction;
            ActionBar.Visibility = openBrowserAction != null ? Visibility.Visible : Visibility.Collapsed;

            _autoCloseTimer.Stop();
            _autoCloseTimer.Interval = TimeSpan.FromSeconds(15);
            _autoCloseTimer.Start();
        });
    }

    public void ShowError(string error)
    {
        Dispatcher.Invoke(() =>
        {
            _pulseAnimation?.Stop();
            StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(239, 68, 68)); // #EF4444
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
