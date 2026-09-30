using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Luma;

public partial class FloatingMusicWindow : Window
{
    private readonly Action<string, double> _command;
    private bool _expanded;
    private bool _seeking;
    private bool _repeat;
    private bool _volumeDragging;
    private long _lastVolumeCommandTicks;
    private double _duration;
    private string _artworkSource = "";
    private bool _positioned;
    private double _compactWidth = 372;
    private double _compactHeight = 82;
    private const double ExpandedWidth = 432;
    private const double ExpandedHeight = 218;

    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    public FloatingMusicWindow(Action<string, double> command)
    {
        _command = command;
        InitializeComponent();
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowProc);
        Loaded += (_, _) => PlaceNearWorkArea();
    }

    // Only the four rounded corners are resize handles. Returning HTCLIENT everywhere else
    // deliberately disables resizing from the straight top/bottom/left/right edges.
    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmNcHitTest) return IntPtr.Zero;
        var packed = lParam.ToInt64();
        var screen = new Point(unchecked((short)(packed & 0xffff)), unchecked((short)((packed >> 16) & 0xffff)));
        var point = PointFromScreen(screen);
        const double grip = 18;
        var hit = point.Y <= grip && point.X <= grip ? HtTopLeft
            : point.Y <= grip && point.X >= ActualWidth - grip ? HtTopRight
            : point.Y >= ActualHeight - grip && point.X <= grip ? HtBottomLeft
            : point.Y >= ActualHeight - grip && point.X >= ActualWidth - grip ? HtBottomRight
            : HtClient;
        handled = true;
        return new IntPtr(hit);
    }

    public void SyncTheme(Window owner)
    {
        Copy(owner, "SurfaceBrush", "FloatSurface");
        Copy(owner, "SurfaceRaisedBrush", "FloatRaised");
        Copy(owner, "SurfaceInsetBrush", "FloatInset");
        Copy(owner, "BorderStrongBrush", "FloatBorder");
        Copy(owner, "AccentBrush", "FloatAccent");
        Copy(owner, "ChromeMuted", "FloatMuted");
        Resources["FloatText"] = Brushes.White;
    }

    private void Copy(Window owner, string source, string target)
    {
        if (owner.TryFindResource(source) is Brush brush) Resources[target] = brush.CloneCurrentValue();
    }

    public void Update(BrowserTab tab)
    {
        TitleText.Text = string.IsNullOrWhiteSpace(tab.MediaTitle) ? "Воспроизведение" : tab.MediaTitle;
        ArtistText.Text = string.IsNullOrWhiteSpace(tab.MediaArtist) ? tab.Domain : tab.MediaArtist;
        PositionText.Text = tab.MediaPositionText;
        DurationText.Text = tab.MediaDurationText;
        _duration = tab.MediaDuration;
        if (!_seeking) SeekSlider.Value = tab.MediaProgress;
        if (!_volumeDragging)
        {
            VolumeSlider.Value = Math.Clamp(tab.MediaVolume, 0, 1);
            VolumeText.Text = $"{Math.Round(VolumeSlider.Value * 100):0}%";
        }
        var icon = (Geometry)FindResource(tab.MediaPlaying ? "IconPause" : "IconPlay");
        ToggleIcon.Data = icon;
        ExpandedToggleIcon.Data = icon;
        SetArtwork(string.IsNullOrWhiteSpace(tab.MediaArtwork) ? tab.FaviconUrl : tab.MediaArtwork);
    }

    public void ShowWithoutActivation()
    {
        if (!_positioned) { PlaceNearWorkArea(); _positioned = true; }
        if (!IsVisible) Show();
    }

    private void SetArtwork(string source)
    {
        if (string.Equals(_artworkSource, source, StringComparison.Ordinal)) return;
        _artworkSource = source;
        try
        {
            if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException();
            var image = new BitmapImage();
            image.BeginInit(); image.DecodePixelWidth = 180; image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(source, UriKind.RelativeOrAbsolute); image.EndInit();
            Artwork.Source = image; Artwork.Visibility = Visibility.Visible; Fallback.Visibility = Visibility.Collapsed;
        }
        catch { Artwork.Source = null; Artwork.Visibility = Visibility.Collapsed; Fallback.Visibility = Visibility.Visible; }
    }

    private void PlaceNearWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left + 12, area.Right - Width - 22);
        Top = Math.Max(area.Top + 12, area.Bottom - Height - 22);
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindInteractive(e.OriginalSource as DependencyObject)) return;
        if (e.ClickCount == 2) ToggleExpanded();
        else
        {
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }
        e.Handled = true;
    }

    private void ToggleExpanded()
    {
        var right = Left + ActualWidth;
        var bottom = Top + ActualHeight;
        if (!_expanded)
        {
            _compactWidth = ActualWidth;
            _compactHeight = ActualHeight;
            _expanded = true;
            MinWidth = 340; MinHeight = 186;
            MaxWidth = 720; MaxHeight = 420;
            Width = ExpandedWidth; Height = ExpandedHeight;
        }
        else
        {
            _expanded = false;
            MinWidth = 260; MinHeight = 72;
            MaxWidth = 720; MaxHeight = 420;
            Width = Math.Clamp(_compactWidth, MinWidth, MaxWidth);
            Height = Math.Clamp(_compactHeight, MinHeight, 110);
        }
        Details.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        DetailsRow.Height = new GridLength(_expanded ? 124 : 0);
        CloseButton.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        ActionColumn.Width = new GridLength(_expanded ? 86 : 46);
        Left = right - Width;
        Top = bottom - Height;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        var top = point.Y <= 12;
        var bottom = point.Y >= ActualHeight - 12;
        if ((top && point.X <= 16) || (bottom && point.X >= ActualWidth - 16)) Cursor = Cursors.SizeNWSE;
        else if ((top && point.X >= ActualWidth - 16) || (bottom && point.X <= 16)) Cursor = Cursors.SizeNESW;
        else Cursor = Cursors.Arrow;
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(this);
        var hit = point.Y <= 14 && point.X <= 18 ? HtTopLeft
            : point.Y <= 14 && point.X >= ActualWidth - 18 ? HtTopRight
            : point.Y >= ActualHeight - 14 && point.X <= 18 ? HtBottomLeft
            : point.Y >= ActualHeight - 14 && point.X >= ActualWidth - 18 ? HtBottomRight : 0;
        if (hit == 0) return;
        ReleaseCapture();
        SendMessage(new WindowInteropHelper(this).Handle, WmNcLButtonDown, new IntPtr(hit), IntPtr.Zero);
        e.Handled = true;
    }

    private static bool FindInteractive(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Slider) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private void Toggle_Click(object sender, RoutedEventArgs e) => _command("toggle", 0);
    private void Close_Click(object sender, RoutedEventArgs e) { _command("dismiss", 0); Hide(); }
    private void Previous_Click(object sender, RoutedEventArgs e) => _command("skip", -1);
    private void Next_Click(object sender, RoutedEventArgs e) => _command("skip", 1);
    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        _repeat = !_repeat;
        RepeatButton.Foreground = _repeat ? (Brush)Resources["FloatAccent"] : (Brush)Resources["FloatMuted"];
        _command("repeat", _repeat ? 1 : 0);
    }
    private void SeekSlider_DragStart(object sender, MouseButtonEventArgs e) => _seeking = true;
    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_seeking && _duration > 0) PositionText.Text = Format(e.NewValue * _duration);
    }
    private void SeekSlider_DragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_seeking) return;
        _seeking = false;
        if (_duration > 0) _command("seek", SeekSlider.Value * _duration);
    }
    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeText is not null) VolumeText.Text = $"{Math.Round(e.NewValue * 100):0}%";
    }
    private void VolumeSlider_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _volumeDragging = true;
        VolumeSlider.CaptureMouse();
        SetVolumeFromPointer(e, true);
        e.Handled = true;
    }
    private void VolumeSlider_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_volumeDragging || e.LeftButton != MouseButtonState.Pressed) return;
        SetVolumeFromPointer(e, false);
        e.Handled = true;
    }
    private void VolumeSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_volumeDragging) return;
        SetVolumeFromPointer(e, true);
        _volumeDragging = false;
        VolumeSlider.ReleaseMouseCapture();
        e.Handled = true;
    }
    private void SetVolumeFromPointer(MouseEventArgs e, bool force)
    {
        var ratio = Math.Clamp(e.GetPosition(VolumeSlider).X / Math.Max(1, VolumeSlider.ActualWidth), 0, 1);
        VolumeSlider.Value = ratio;
        VolumeText.Text = $"{Math.Round(ratio * 100):0}%";
        var now = Environment.TickCount64;
        if (force || now - _lastVolumeCommandTicks >= 45)
        {
            _lastVolumeCommandTicks = now;
            _command("volume", ratio);
        }
    }
    private static string Format(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.Hours > 0 ? $"{time.Hours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
    }
}
